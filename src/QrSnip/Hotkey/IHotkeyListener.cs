using System;

namespace QrSnip.Hotkey;

// The hotkey seam.
//
// Earns its interface on two of the three filter rules:
//   - Rule 2 (untestable without): real registration claims a global hotkey
//     that interferes with the developer's machine during tests.
//   - Rule 3 (likely-to-be-swapped): unlikely, but theoretically. The Stage 0
//     decision document accepted that we may swap implementations in the
//     future if the LL-hook path becomes necessary.
//
// Lifecycle: construct once, TryRegister returns success/conflict/other-error,
// HotkeyPressed fires on every press until Unregister or Dispose. Re-binding
// is Unregister + TryRegister; same instance is reusable.
public interface IHotkeyListener : IDisposable
{
    // Fires every time the registered hotkey is pressed. Subscribers run on
    // the WPF Dispatcher thread (WM_HOTKEY is delivered via the message pump).
    event EventHandler HotkeyPressed;

    // Attempts to register the given combination as a global hotkey.
    // Distinguishes "another process owns it" from "registration failed for
    // some other reason" so the caller can show actionable error UX.
    HotkeyRegistrationResult TryRegister(Hotkey hotkey);

    // Releases the currently-registered hotkey if any. No-op if not registered.
    void Unregister();

    // The currently-registered hotkey, or null if Unregister/never-registered.
    Hotkey? Current { get; }
}
