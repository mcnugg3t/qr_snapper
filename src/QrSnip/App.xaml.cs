using System;
using System.Windows;
using QrSnip.Capture;
using QrSnip.Clipboard;
using QrSnip.Decoding;
using QrSnip.Decoding.Preprocessors;
using QrSnip.Hotkey;
using QrSnip.Interop;
using QrSnip.Notifications;
using QrSnip.Overlay;
using QrSnip.Settings;

namespace QrSnip;

public partial class App : Application
{
    private SettingsService? _settings;
    private TrayIcon? _tray;
    private IHotkeyListener? _hotkeyListener;
    private AutoStartService? _autoStart;

    // The snip pipeline dependencies. Constructed once at startup so each
    // hotkey press just spins up a fresh SnipSession with them.
    private IScreenCapture? _screenCapture;
    private IQrDecoder? _qrDecoder;
    private IClipboardService? _clipboard;
    private AutoPasteService? _autoPaste;
    private IToastNotifier? _toast;

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
            // Migrate any pre-rename AppData folders (QrSnip -> QRSnapper).
            // Must run BEFORE SettingsService construction so it picks up
            // the moved config.json from the new path.
            AppDataMigration.Run();

            _settings = new SettingsService();
            // Now that settings exist, gate verbose logging on DebugMode.
            Diagnostics.SetVerboseGate(() => _settings.Current.DebugMode);

            // Composition root: instantiate the snip pipeline once.
            // The decoder is a PreprocessingQrDecoder wrapping ZXingCppQrDecoder.
            // The C++ port's locator handles eroded/blurry finder patterns much
            // better than the .NET port; for the clean-input case it's also
            // faster. Preprocessor ladder still wraps it as a fallback for the
            // hardest inputs.
            _screenCapture = new WgcScreenCapture();
            _qrDecoder = new PreprocessingQrDecoder(
                new ZXingCppQrDecoder(),
                DefaultPreprocessorLadder.Build());
            _clipboard = new WindowsClipboardService();
            _autoPaste = new AutoPasteService();

            // AutoStartService needs the EXE path so it can write the right
            // entry in HKCU\Run. Environment.ProcessPath is the absolute
            // path to the currently-running executable.
            _autoStart = new AutoStartService(Environment.ProcessPath ?? "QRSnapper.exe");

            WireUpHotkey();

            // TrayIcon needs the dependencies for the Settings... menu item
            // to open the SettingsWindow. Constructed last so all wiring is in
            // place before the menu becomes interactive.
            _tray = new TrayIcon(_settings, _hotkeyListener!, _autoStart);

            // Toast notifier reuses the TaskbarIcon owned by TrayIcon — so
            // it has to be constructed AFTER the tray. Lifetime is tied to
            // the tray; the tray's Dispose tears down the TaskbarIcon and
            // any pending toasts disappear with it.
            _toast = new TrayToastNotifier(_tray.TaskbarIcon);
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("App.OnStartup", ex);
            MessageBox.Show(
                $"QR Snapper failed to start.\n\n{ex.Message}\n\nSee %LOCALAPPDATA%\\QRSnapper\\startup.log for details.",
                "QR Snapper startup error",
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
            Diagnostics.LogVerbose("Hotkey fired -> SnipSession");
            try
            {
                // Capture the active window NOW, before the overlay steals
                // focus. Used by auto-paste to restore focus before
                // synthesizing Ctrl+V. Recapturing later would just give
                // us our own OverlayWindow.
                var autoPasteTarget = _autoPaste!.GetForegroundWindowHandle();
                var snapshot = _settings!.Current;

                var session = new SnipSession(
                    _screenCapture!, _qrDecoder!, _clipboard!,
                    _autoPaste, _toast!,
                    snapshot.AutoPasteEnabled,
                    snapshot.AutoPasteAppendKey,
                    snapshot.ShowToastsOnSuccess,
                    autoPasteTarget);
                await session.RunAsync();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("SnipSession.RunAsync", ex);
            }
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
