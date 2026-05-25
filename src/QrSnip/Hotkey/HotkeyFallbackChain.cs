using System.Collections.Generic;

namespace QrSnip.Hotkey;

// The startup fallback chain used when the persisted hotkey can't be claimed
// (or no hotkey is persisted yet). Per Stage 0's findings, Win+Shift+Q is the
// preferred default; the others are graceful degradations if it's taken.
//
// Tries each in order. Returns the first one that registers successfully,
// along with the listener so the caller can use it. Throws if NONE register
// — at that point the user has bigger problems and the app should surface
// the failure loudly.
internal static class HotkeyFallbackChain
{
    private const uint VK_Q = 0x51;

    public static IReadOnlyList<Hotkey> Defaults { get; } = new[]
    {
        new Hotkey(KeyModifiers.Win     | KeyModifiers.Shift, VK_Q), // Win+Shift+Q
        new Hotkey(KeyModifiers.Control | KeyModifiers.Alt,   VK_Q), // Ctrl+Alt+Q
        new Hotkey(KeyModifiers.Control | KeyModifiers.Shift, VK_Q), // Ctrl+Shift+Q
    };

    // Tries the persisted hotkey first (if any), then walks the defaults
    // skipping any that match the persisted one. Returns the hotkey that
    // successfully registered, or null if none could be claimed.
    public static Hotkey? RegisterFirstAvailable(IHotkeyListener listener, Hotkey? preferred)
    {
        if (preferred is not null
            && listener.TryRegister(preferred) == HotkeyRegistrationResult.Success)
        {
            return preferred;
        }

        foreach (var candidate in Defaults)
        {
            if (preferred is not null && candidate.Equals(preferred)) continue;
            if (listener.TryRegister(candidate) == HotkeyRegistrationResult.Success)
            {
                return candidate;
            }
        }
        return null;
    }
}
