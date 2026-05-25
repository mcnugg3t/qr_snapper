using System;
using System.Threading.Tasks;

namespace QrSnip.Clipboard;

// The retry loop, extracted from WindowsClipboardService so it can be tested
// against fake set-operations without touching the real OS clipboard. This is
// the abstraction filter at finer grain: WindowsClipboardService doesn't earn
// a richer seam (one OS, one implementation), but the retry POLICY does earn
// a seam because we want to verify it gives up at the right count, waits the
// right amount, etc., independent of the clipboard touching code.
internal static class ClipboardRetry
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan BackoffPerAttempt = TimeSpan.FromMilliseconds(80);

    // Retries the operation up to MaxAttempts times with BackoffPerAttempt
    // between attempts. The delayProvider parameter exists so tests can pass
    // a no-op delay to avoid actually waiting ~400ms per test.
    public static async Task<bool> TryAsync(
        Func<Task<bool>> operation,
        Func<TimeSpan, Task>? delayProvider = null)
    {
        delayProvider ??= Task.Delay;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (await operation())
            {
                return true;
            }
            if (attempt < MaxAttempts)
            {
                await delayProvider(BackoffPerAttempt);
            }
        }
        Diagnostics.Log($"ClipboardRetry gave up after {MaxAttempts} attempts.");
        return false;
    }
}
