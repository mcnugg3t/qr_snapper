using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace QrSnip.Clipboard;

// Real System.Windows.Clipboard wrapper with retry. The clipboard is a single
// shared OS resource: Clipboard.SetText throws CLIPBRD_E_CANT_OPEN when
// another process holds it (typically a few hundred ms during a paste).
// 5 tries x 80ms backoff covers the typical contention window without
// noticeably delaying the user on the happy path.
public sealed class WindowsClipboardService : IClipboardService
{
    public Task<bool> TrySetTextAsync(string text)
    {
        return ClipboardRetry.TryAsync(() => DispatchAndSet(text));
    }

    // Clipboard access requires the STA UI thread. Marshal via the dispatcher
    // so callers can fire-and-forget from any context. SetDataObject with
    // copy:true is more robust than bare SetText (more durable across the
    // app shutting down immediately after copying).
    private static Task<bool> DispatchAndSet(string text)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // No WPF app context (likely during a test). Run on the calling
            // thread; the caller is responsible for being STA in that case.
            return Task.FromResult(TrySetCore(text));
        }
        return dispatcher.InvokeAsync(() => TrySetCore(text), DispatcherPriority.Normal).Task;
    }

    private static bool TrySetCore(string text)
    {
        try
        {
            System.Windows.Clipboard.SetDataObject(text, copy: true);
            return true;
        }
        catch
        {
            // Any clipboard exception is treated as transient/retryable.
            // The interesting one is CLIPBRD_E_CANT_OPEN (HRESULT 0x800401D0),
            // raised when another process holds the clipboard.
            return false;
        }
    }
}
