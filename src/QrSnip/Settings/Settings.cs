namespace QrSnip.Settings;

// Alias to avoid the "namespace vs type" name collision when declaring
// `Hotkey? Hotkey = null` below — `Hotkey` would resolve to the namespace.
using HotkeyValue = QrSnip.Hotkey.Hotkey;

// User-configurable settings, serialized to %APPDATA%\QrSnip\config.json.
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
    HotkeyValue? Hotkey = null);
