using System.Threading.Tasks;

namespace QrSnip.Clipboard;

// The clipboard seam. Justifies its interface on testability alone (rule 2):
// real Clipboard.SetText touches global OS state and would interfere with the
// developer's actual clipboard during tests.
//
// Returns bool rather than throwing because clipboard contention is an
// expected failure mode (some other app holds the clipboard mid-paste), not
// an error condition worth an exception.
public interface IClipboardService
{
    // Tries to place text on the system clipboard, retrying briefly to ride
    // out transient contention. Returns true on success, false if the
    // retry budget was exhausted.
    //
    // Callers should treat false as a user-visible failure: a paste that
    // happens after a failed copy will return whatever stale clipboard
    // contents were there before, which is the worst possible silent bug.
    // Stage 6's toast UX overrides "toasts disabled" for this case.
    Task<bool> TrySetTextAsync(string text);
}
