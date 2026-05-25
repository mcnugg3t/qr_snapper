using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QrSnip.Hotkey;

// Win32 RegisterHotKey-based IHotkeyListener implementation.
//
// Owns a hidden message-only HwndSource so WM_HOTKEY messages have somewhere
// to be delivered. The HwndSource hook runs on the WPF Dispatcher thread, so
// HotkeyPressed handlers don't need to marshal.
//
// Why a HwndSource rather than ComponentDispatcher.ThreadFilterMessage:
// HwndSource gives us a stable HWND we can pass to RegisterHotKey. With
// ThreadFilterMessage we'd be passing IntPtr.Zero (current-thread hotkey),
// which works but couples message routing to whichever thread we happen to
// be on. HwndSource is more explicit.
internal sealed class RegisterHotKeyListener : IHotkeyListener
{
    // Fixed hotkey ID per process. We only ever register one hotkey, so a
    // constant is fine. (If we ever needed multiple, we'd allocate IDs.)
    private const int HotkeyId = 0xC0DE;

    private const int WM_HOTKEY = 0x0312;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_NOREPEAT = 0x4000;

    private HwndSource? _hwndSource;
    private Hotkey? _current;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public Hotkey? Current => _current;

    public RegisterHotKeyListener()
    {
        // Hidden message-only window. HWND_MESSAGE (-3) means it doesn't
        // participate in z-order, doesn't get painted, and only exists to
        // receive messages.
        var parameters = new HwndSourceParameters("QrSnip.HotkeyListener")
        {
            HwndSourceHook = WndProc,
            ParentWindow = new IntPtr(-3),
        };
        _hwndSource = new HwndSource(parameters);
    }

    public HotkeyRegistrationResult TryRegister(Hotkey hotkey)
    {
        ThrowIfDisposed();
        Unregister();

        var ok = RegisterHotKey(_hwndSource!.Handle, HotkeyId, (uint)hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey);
        if (ok)
        {
            _current = hotkey;
            Diagnostics.LogVerbose($"Hotkey registered: {hotkey.Display}");
            return HotkeyRegistrationResult.Success;
        }

        var err = Marshal.GetLastWin32Error();
        if (err == ERROR_HOTKEY_ALREADY_REGISTERED)
        {
            Diagnostics.Log($"Hotkey {hotkey.Display} unavailable: claimed by another process.");
            return HotkeyRegistrationResult.AlreadyInUse;
        }

        Diagnostics.LogException("RegisterHotKey", new Win32Exception(err));
        return HotkeyRegistrationResult.OtherError;
    }

    public void Unregister()
    {
        if (_current is null || _hwndSource is null) return;
        UnregisterHotKey(_hwndSource.Handle, HotkeyId);
        Diagnostics.LogVerbose($"Hotkey unregistered: {_current.Display}");
        _current = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == HotkeyId)
        {
            handled = true;
            // Fire on the dispatcher thread (we already are — HwndSource hooks
            // run on the thread that owns the HWND, which is our UI thread).
            try
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Never let a subscriber exception kill the message pump.
                Diagnostics.LogException("HotkeyPressed handler", ex);
            }
        }
        return IntPtr.Zero;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RegisterHotKeyListener));
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _hwndSource?.Dispose();
        _hwndSource = null;
        _disposed = true;
    }
}
