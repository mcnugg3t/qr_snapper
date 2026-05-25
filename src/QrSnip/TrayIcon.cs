using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using QrSnip.Capture;
using QrSnip.Settings;

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
    private MenuItem? _testCaptureItem;

    public TrayIcon(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;

        Diagnostics.LogVerbose("TrayIcon ctor: loading icon");
        _ownedIcon = LoadIconWithFallback();
        Diagnostics.LogVerbose("TrayIcon ctor: constructing TaskbarIcon");
        _taskbar = new TaskbarIcon
        {
            ToolTipText = "QrSnip",
            Icon = _ownedIcon,
            ContextMenu = BuildContextMenu(),
        };

        // H.NotifyIcon 2.3.0 quirk: constructing TaskbarIcon in code (rather than
        // XAML) does NOT call Shell_NotifyIcon ADD. The icon won't appear in the
        // system tray until ForceCreate is called explicitly. `enablesEfficiencyMode:
        // false` keeps the process from being throttled by Windows when the icon is
        // the only UI surface (we still need the message pump alive for hotkeys).
        Diagnostics.LogVerbose("TrayIcon ctor: calling ForceCreate");
        _taskbar.ForceCreate(enablesEfficiencyMode: false);
        Diagnostics.LogVerbose("TrayIcon ctor: done");
    }

    public void ShowSettings()
    {
        // Stage 6 hookup. For now we just log so we can confirm the
        // second-instance signaling actually delivers.
        Diagnostics.LogVerbose("TrayIcon.ShowSettings invoked (no SettingsWindow yet).");
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "Settings...", IsEnabled = false };
        // Wired in Stage 6 when SettingsWindow exists.
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

        var quitItem = new MenuItem { Header = "Quit QrSnip" };
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
