using System;
using System.Text.Json.Serialization;

namespace QrSnip.Hotkey;

[Flags]
public enum KeyModifiers : uint
{
    None    = 0,
    Alt     = 0x0001,
    Control = 0x0002,
    Shift   = 0x0004,
    Win     = 0x0008,
}

// A hotkey combination. VirtualKey is a Win32 VK_* code (VK_Q = 0x51).
// Persisted as { modifiers: int, virtualKey: int } in config.json.
public sealed record Hotkey(KeyModifiers Modifiers, uint VirtualKey)
{
    // Human-readable label for the UI and logs. "Win+Shift+Q".
    // [JsonIgnore] because Display is derived from Modifiers + VirtualKey;
    // persisting it would let manual edits drift the rendered label out of
    // sync with the actual binding.
    [JsonIgnore]
    public string Display
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Modifiers.HasFlag(KeyModifiers.Win))     parts.Add("Win");
            if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(KeyModifiers.Alt))     parts.Add("Alt");
            if (Modifiers.HasFlag(KeyModifiers.Shift))   parts.Add("Shift");
            parts.Add(VkToLabel(VirtualKey));
            return string.Join("+", parts);
        }
    }

    private static string VkToLabel(uint vk)
    {
        // Letters and digits map directly; everything else falls through to
        // a hex code. We only ship a handful of bindings, but the Settings
        // UI in Stage 6 will need to display arbitrary captured keys.
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();         // 0..9
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();         // A..Z
        if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x6F}";               // F1..F24
        return $"VK_{vk:X2}";
    }
}

public enum HotkeyRegistrationResult
{
    Success,
    AlreadyInUse,
    OtherError,
}
