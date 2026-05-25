using System.Threading.Tasks;

namespace QrSnip.Capture;

// The screen-capture seam. Earns its interface on rule 2 (untestable without
// one — WGC requires a real display) and rule 3 (likely-to-be-swapped — if
// Win2D + WGC misbehaves under packaged identity, we may swap the readback
// implementation without changing callers).
//
// Returns one CapturedMonitor per HMONITOR. The overlay layer places one
// window per CapturedMonitor at its DesktopBounds and renders Pixels under
// a dim overlay; the user's selection rect maps 1:1 onto Pixels.
public interface IScreenCapture
{
    Task<CapturedMonitor[]> CaptureAllMonitorsAsync();
}

// One frozen monitor frame. All coordinates are in PHYSICAL PIXELS (not DIPs).
// The freeze-frame design (CLAUDE.md §3) means the overlay paints Pixels
// directly, and the user's selection rect maps 1:1 onto Pixels — no
// virtual-desktop coordinate translation needed at decode time.
public sealed record CapturedMonitor(
    // Position of this monitor on the Windows virtual desktop, in physical
    // pixels. May be negative (monitors arranged left of the primary).
    int DesktopX,
    int DesktopY,
    // Captured frame dimensions and BGRA pixel buffer. Stride may exceed
    // Width*4 (row padding from the GPU-side texture).
    int Width,
    int Height,
    int Stride,
    byte[] Pixels,
    // DPI scale of this monitor at capture time. 1.0 == 100%, 1.5 == 150%, etc.
    // Used by the overlay to size itself in DIPs while displaying physical-
    // pixel content.
    double DpiScale);
