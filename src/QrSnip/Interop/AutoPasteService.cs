using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using QrSnip.Settings;

namespace QrSnip.Interop;

// Synthesizes a Ctrl+V keystroke into the previously-focused window so the
// snip workflow can "paste" the decoded text without the user pressing
// anything extra.
//
// Why this is a separate service rather than inline in SnipSession:
//   - It's pure Win32 interop and belongs in Interop/ per project convention.
//   - It's testable in isolation (capture the target HWND + payload, verify
//     the SendInput sequence we constructed) without invoking the real OS.
//   - Stage 7's MSIX packaging may need to declare extra capabilities for
//     this feature; centralizing it makes that easier to audit.
//
// Why concrete class, no interface: single implementation, single consumer,
// no swap planned. Per the abstraction filter, don't pre-extract.
public sealed class AutoPasteService
{
    // Captures the currently-foreground window. SnipSession calls this at
    // the START of the snip workflow (before the overlay grabs focus) and
    // hands the result back to PasteToWindow when ready.
    public IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

    // Restores focus to the captured target window and synthesizes a
    // Ctrl+V keystroke, optionally followed by `appendKey` (e.g. Tab to
    // advance to the next form field in a lab software workflow).
    // Returns true on best-effort success — Windows input synthesis can
    // silently no-op in elevated contexts (UIPI), so we can't actually
    // verify the paste landed.
    public async Task<bool> PasteToWindowAsync(IntPtr targetWindow, AutoPasteAppendKey appendKey = AutoPasteAppendKey.None)
    {
        if (targetWindow == IntPtr.Zero)
        {
            Diagnostics.LogVerbose("AutoPaste: no target window captured");
            return false;
        }

        // Restore focus to the original window. Without this, our overlay
        // is still the "most recently active" window in the OS's mind and
        // the keystroke would land in nowhere.
        var brought = SetForegroundWindow(targetWindow);
        if (!brought)
        {
            Diagnostics.Log($"AutoPaste: SetForegroundWindow({targetWindow.ToInt64():X}) returned false");
            // We continue anyway — SetForegroundWindow can return false even
            // when it works, and the worst case is the paste goes nowhere.
        }

        // Brief delay so the OS has time to actually rebind focus + the
        // target app's message pump can start receiving our input. 50ms is
        // empirically reliable; less and we sometimes lose the first event.
        await Task.Delay(50);

        var inputs = BuildCtrlVSequence(appendKey);
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            var err = Marshal.GetLastWin32Error();
            Diagnostics.Log($"AutoPaste: SendInput sent {sent}/{inputs.Length} events, error={err}");
            return false;
        }

        Diagnostics.LogVerbose($"AutoPaste: pasted into window 0x{targetWindow.ToInt64():X} (appendKey={appendKey})");
        return true;
    }

    // Builds the keyboard event sequence: Ctrl+V always, optionally followed
    // by a single trailing key (Tab/Enter for workflow integration). Each
    // event is an INPUT struct with KEYBDINPUT payload.
    private static INPUT[] BuildCtrlVSequence(AutoPasteAppendKey appendKey)
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V       = 0x56;
        const ushort VK_TAB     = 0x09;
        const ushort VK_RETURN  = 0x0D;
        const uint KEYEVENTF_KEYUP = 0x0002;

        var events = new List<INPUT>(6)
        {
            MakeKey(VK_CONTROL, flags: 0),
            MakeKey(VK_V, flags: 0),
            MakeKey(VK_V, flags: KEYEVENTF_KEYUP),
            MakeKey(VK_CONTROL, flags: KEYEVENTF_KEYUP),
        };

        // Append the trailing key AFTER releasing Ctrl, so Tab/Enter
        // doesn't get interpreted as Ctrl+Tab / Ctrl+Enter (which would
        // do something completely different in most apps).
        var appendVk = appendKey switch
        {
            AutoPasteAppendKey.Tab   => VK_TAB,
            AutoPasteAppendKey.Enter => VK_RETURN,
            _ => (ushort)0,
        };
        if (appendVk != 0)
        {
            events.Add(MakeKey(appendVk, flags: 0));
            events.Add(MakeKey(appendVk, flags: KEYEVENTF_KEYUP));
        }

        return events.ToArray();
    }

    private static INPUT MakeKey(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    // --- Win32 declarations ---

    private const uint INPUT_KEYBOARD = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // The INPUT struct in Win32 is a union of three payloads; we only use
    // the keyboard one. Using an explicit-layout struct so the unused
    // mouse/hardware union slots are zero-initialized correctly without
    // taking extra memory.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
