namespace QrSnip.Settings;

// Alias to avoid the "namespace vs type" name collision when declaring
// `Hotkey? Hotkey = null` below — `Hotkey` would resolve to the namespace.
using HotkeyValue = QrSnip.Hotkey.Hotkey;

// User-configurable settings, serialized to %APPDATA%\QRSnapper\config.json.
//
// This record is the persisted shape. Adding a property: add it here with a
// sensible default. System.Text.Json handles forward compatibility — older
// config files missing the new property will deserialize with the default.
//
// Designed as an immutable record so consumers can capture a snapshot and
// not worry about racing reload events.
public sealed record Settings(
    // When true, exposes the "Test Capture (debug)" tray menu item and
    // enables verbose Diagnostics.Log calls in capture/decoder paths.
    // Default false: quiet operation for non-technical users.
    bool DebugMode = false,

    // The hotkey that triggers a snip. Null means "use the default fallback
    // chain on startup" — App.OnStartup tries Win+Shift+Q first, then
    // Ctrl+Alt+Q, etc., and writes whichever one succeeds back to disk.
    // After the first successful run this is always populated.
    HotkeyValue? Hotkey = null,

    // When true, QR Snapper launches automatically at Windows login.
    // Persisted in settings AND mirrored to HKCU\...\Run on save so the OS
    // and the app stay in sync. Default false — the user opts in.
    bool AutoStartEnabled = false,

    // When true, after a successful snip we restore focus to the window
    // that was active before the hotkey fired and synthesize a Ctrl+V
    // paste into it. Default false because accidental pastes into the
    // wrong app are annoying; this is a power-user opt-in.
    bool AutoPasteEnabled = false,

    // When true, a tray balloon/toast is shown after a successful snip
    // with the decoded payload (truncated). Default false per the
    // quiet-by-default principle — the clipboard update / blue flash is
    // the primary signal. Power users who want explicit confirmation can
    // opt in.
    //
    // NOTE: a toast is ALWAYS shown on clipboard failure regardless of
    // this setting, because silent clipboard failure is the worst possible
    // bug (user pastes stale content thinking it was the snip).
    bool ShowToastsOnSuccess = false);
