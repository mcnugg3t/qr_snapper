using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using QrSnip.Capture;
using QrSnip.Hotkey;
using QrSnip.Settings;
using QrSnip.SettingsUi;

namespace QrSnip;

// The tray-icon owner. Holds the TaskbarIcon, wires its context menu, and
// owns the app shutdown action. Disposed by the composition root on shutdown.
//
// Behavior decisions:
//   - Left/double-click do nothing for now. In Stage 6 we'll wire double-click
//     to "open settings". We do NOT inherit any H.NotifyIcon defaults: every
//     interactive behavior is opted in explicitly.
//   - Context menu has only the items we add. Settings is present but disabled
//     until Stage 6 instantiates a SettingsWindow.
//   - We set TaskbarIcon.Icon (System.Drawing.Icon) directly, not IconSource.
//     IconSource takes a WPF ImageSource and internally converts to Icon, which
//     loses ICO frame selection (BitmapImage only decodes the first frame).
//     Going straight to System.Drawing.Icon lets us pick the right frame for
//     the current tray DPI.
internal sealed class TrayIcon : IDisposable
{
    private const string EmbeddedIconName = "qr_snapper_icon.ico";
    private readonly TaskbarIcon _taskbar;
    private readonly Icon? _ownedIcon;
    private readonly SettingsService _settings;
    private readonly IHotkeyListener _hotkeyListener;
    private readonly AutoStartService _autoStart;
    private MenuItem? _testCaptureItem;
    private SettingsWindow? _settingsWindow;

    // Exposed so App.xaml.cs can construct a TrayToastNotifier from the
    // same TaskbarIcon — keeping toast lifetime tied to the tray icon's.
    internal TaskbarIcon TaskbarIcon => _taskbar;

    public TrayIcon(SettingsService settings, IHotkeyListener hotkeyListener, AutoStartService autoStart)
    {
        _settings = settings;
        _hotkeyListener = hotkeyListener;
        _autoStart = autoStart;
        _settings.Changed += OnSettingsChanged;

        Diagnostics.LogVerbose("TrayIcon ctor: loading icon");
        _ownedIcon = LoadIconWithFallback();
        Diagnostics.LogVerbose("TrayIcon ctor: constructing TaskbarIcon");
        _taskbar = new TaskbarIcon
        {
            ToolTipText = "QR Snapper",
            Icon = _ownedIcon,
            ContextMenu = BuildContextMenu(),
        };

        // H.NotifyIcon 2.3.0 quirk: constructing TaskbarIcon in code (rather than
        // XAML) does NOT call Shell_NotifyIcon ADD. The icon won't appear in the
        // system tray until ForceCreate is called explicitly. `enablesEfficiencyMode:
        // false` keeps the process from being throttled by Windows when the icon is
        // the only UI surface (we still need the message pump alive for hotkeys).
        //
        // ForceCreate can fail with "TryCreate failed" when the Windows shell
        // hasn't finished settling — e.g., immediately after an installer
        // completes, the tray is busy refreshing its cache. We retry up to 3
        // times with 250ms backoff before giving up. The library's own retry
        // does a "TryDelete first" check; this adds wait-and-retry on top.
        Diagnostics.LogVerbose("TrayIcon ctor: calling ForceCreate");
        ForceCreateWithRetry();
        Diagnostics.LogVerbose("TrayIcon ctor: done");
    }

    private void ForceCreateWithRetry()
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _taskbar.ForceCreate(enablesEfficiencyMode: false);
                if (attempt > 1)
                {
                    Diagnostics.Log($"ForceCreate succeeded on attempt {attempt}/{maxAttempts}");
                }
                return;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("TryCreate"))
            {
                if (attempt == maxAttempts)
                {
                    Diagnostics.LogException($"ForceCreate failed after {maxAttempts} attempts", ex);
                    throw;
                }
                Diagnostics.Log($"ForceCreate attempt {attempt}/{maxAttempts} failed (shell not ready?); retrying in 250ms");
                System.Threading.Thread.Sleep(250);
            }
        }
    }

    public void ShowSettings()
    {
        // Single SettingsWindow instance — if the user clicks Settings...
        // while one is already open, we just bring it to the front instead
        // of opening a second one.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _hotkeyListener, _autoStart);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        // The "Test Capture (debug)" item is visible only when DebugMode is on.
        // Keeping it on the menu (not building/tearing-down the whole menu on
        // settings changes) means we just toggle Visibility — simpler and
        // preserves any menu state.
        _testCaptureItem = new MenuItem { Header = "Test Capture (debug)" };
        _testCaptureItem.Click += async (_, _) => await TestCapture.RunAsync();
        _testCaptureItem.Visibility = _settings.Current.DebugMode ? Visibility.Visible : Visibility.Collapsed;
        menu.Items.Add(_testCaptureItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quit QR Snapper" };
        quitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        return menu;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // SettingsService raises Changed from a thread-pool thread; marshal
        // to the UI thread before touching WPF state.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_testCaptureItem is not null)
            {
                _testCaptureItem.Visibility = _settings.Current.DebugMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        });
    }

    private static Icon LoadIconWithFallback()
    {
        try
        {
            // Pick the DPI-appropriate tray size. Standard system tray is
            // 16x16 at 100%, 32x32 at 200%. SystemParameters.SmallIconWidth
            // already accounts for scaling.
            var size = (int)SystemParameters.SmallIconWidth;
            using var stream = OpenEmbeddedIcon();
            var icon = new Icon(stream, size, size);
            return icon;
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("LoadIcon", ex);
            // SystemIcons.Application returns a shared reference we must not
            // dispose; clone it so our ownership story stays consistent.
            var fallback = (Icon)SystemIcons.Application.Clone();
            Diagnostics.Log("Tray icon load failed; using SystemIcons.Application fallback.");
            return fallback;
        }
    }

    private static Stream OpenEmbeddedIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        var stream = asm.GetManifestResourceStream(EmbeddedIconName);
        if (stream is null)
        {
            // Help future-me diagnose embed-failures: dump what's actually there.
            var names = string.Join(", ", asm.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedIconName}' not found. Available: [{names}]");
        }
        return stream;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _taskbar.Dispose();
        _ownedIcon?.Dispose();
    }
}
