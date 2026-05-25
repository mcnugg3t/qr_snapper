using System;
using System.Windows;
using QrSnip.Capture;
using QrSnip.Hotkey;
using QrSnip.Settings;

namespace QrSnip;

public partial class App : Application
{
    private SettingsService? _settings;
    private TrayIcon? _tray;
    private IHotkeyListener? _hotkeyListener;

    // The tray icon owner. Exposed so Program.cs can wire the second-instance
    // signal to it after Startup completes.
    internal TrayIcon? Tray => _tray;

    // Exposed so Program.cs can wire Diagnostics.LogVerbose to it before the
    // app starts logging from background threads.
    internal SettingsService? SettingsService => _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create the tray icon INSIDE OnStartup, not in Program.cs before Run().
        // TaskbarIcon needs a live WPF dispatcher pump to successfully register
        // with the system tray via Shell_NotifyIcon; constructing it pre-Run()
        // can silently fail to show even when the icon data loads fine.
        try
        {
            _settings = new SettingsService();
            // Now that settings exist, gate verbose logging on DebugMode.
            Diagnostics.SetVerboseGate(() => _settings.Current.DebugMode);

            _tray = new TrayIcon(_settings);
            WireUpHotkey();
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("App.OnStartup", ex);
            MessageBox.Show(
                $"QrSnip failed to start.\n\n{ex.Message}\n\nSee %LOCALAPPDATA%\\QrSnip\\startup.log for details.",
                "QrSnip startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // Constructs the hotkey listener, registers the persisted hotkey (or the
    // fallback chain if none is persisted or it can't be claimed), and
    // persists the successfully-registered combo back to disk. Surfaces a
    // tray balloon if nothing in the fallback chain works.
    private void WireUpHotkey()
    {
        _hotkeyListener = new RegisterHotKeyListener();
        _hotkeyListener.HotkeyPressed += async (_, _) =>
        {
            Diagnostics.LogVerbose("Hotkey fired");
            await TestCapture.RunAsync();
        };

        var registered = HotkeyFallbackChain.RegisterFirstAvailable(_hotkeyListener, _settings!.Current.Hotkey);
        if (registered is null)
        {
            Diagnostics.Log("All hotkey candidates failed to register. Snipping is disabled until rebind in Stage 6's settings UI.");
            // Stage 6 will show a tray balloon + open settings here.
            return;
        }

        if (!registered.Equals(_settings.Current.Hotkey))
        {
            Diagnostics.Log($"Registered hotkey '{registered.Display}' (preferred '{_settings.Current.Hotkey?.Display ?? "<none>"}' was unavailable). Persisting.");
            _settings.Save(_settings.Current with { Hotkey = registered });
        }
        else
        {
            Diagnostics.LogVerbose($"Registered persisted hotkey: {registered.Display}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyListener?.Dispose();
        _tray?.Dispose();
        _settings?.Dispose();
        base.OnExit(e);
    }
}
