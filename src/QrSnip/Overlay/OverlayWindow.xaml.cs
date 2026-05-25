using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QrSnip.Capture;

namespace QrSnip.Overlay;

// One per monitor. Renders the frozen monitor image full-bleed, with a dim
// overlay everywhere except inside the user's drag-selection rectangle.
//
// Coordinate conventions (CLAUDE.md §6 Problem 4):
//   - All MouseEventArgs.GetPosition() results are in WPF DIPs relative to
//     this window's top-left corner. Variables carrying DIP values use the
//     suffix `_dip`.
//   - The crop rectangle handed to the decoder must be in PHYSICAL PIXELS
//     of the original capture buffer. Variables carrying PX values use `_px`.
//   - The single DIP → PX conversion lives in GetSelectionInPixels(); every
//     other coordinate stays in DIPs.
//
// Lifecycle:
//   - Constructed by SnipSession with a CapturedMonitor.
//   - SnipSession calls Show(), which positions the window over its monitor.
//   - User drags → mouse-up fires Completed event with the cropped pixels.
//   - User presses ESC → Cancelled event fires.
//   - SnipSession closes/disposes all overlays after either signal.
public partial class OverlayWindow : Window
{
    private readonly CapturedMonitor _monitor;
    private readonly WriteableBitmap _frozenBitmap;

    private bool _isDragging;
    private Point _dragStart_dip;
    private Point _dragCurrent_dip;

    // Fires when the user releases the mouse after a selection. Carries
    // the cropped BGRA pixels in PHYSICAL PIXEL space, ready for IQrDecoder.
    public event EventHandler<SelectionCompletedEventArgs>? Completed;

    // Fires when the user presses ESC or clicks without dragging a useful
    // rectangle. SnipSession should tear down ALL overlays in response.
    public event EventHandler? Cancelled;

    public OverlayWindow(CapturedMonitor monitor)
    {
        InitializeComponent();
        _monitor = monitor;
        _frozenBitmap = MakeFrozenBitmap(monitor);
        FrozenImage.Source = _frozenBitmap;

        // Position the window to exactly cover its monitor. WPF Window.Left/
        // Top are in DIPs of the PRIMARY monitor, but per-monitor-DPI-v2
        // means WPF treats virtual-desktop coordinates as physical pixels
        // when WindowStartupLocation=Manual. The captured DesktopX/Y are
        // already physical pixels, so we set Left/Top from those directly
        // and let WPF do its own DPI math.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.DesktopX / monitor.DpiScale;
        Top = monitor.DesktopY / monitor.DpiScale;
        Width = monitor.Width / monitor.DpiScale;
        Height = monitor.Height / monitor.DpiScale;

        // Selection fill mirrors the frozen image at full brightness inside
        // the selection rectangle. We use an ImageBrush directly over the
        // WriteableBitmap rather than a VisualBrush over the FrozenImage
        // element. VisualBrush captures the source element's RENDERED
        // visual tree — including any elements drawn on top of it — which
        // causes a recursive nested-rectangle artifact inside the selection.
        // ImageBrush operates on the bitmap directly, no recursion possible.
        SelectionFill.Fill = new ImageBrush(_frozenBitmap)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };

        // Subscribe to window-level mouse + keyboard events. Each event
        // updates the small state machine: idle → dragging → completed/cancelled.
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Loaded += (_, _) => Activate(); // ensure keyboard focus so ESC works
    }

    private static WriteableBitmap MakeFrozenBitmap(CapturedMonitor m)
    {
        var bmp = new WriteableBitmap(m.Width, m.Height, dpiX: 96, dpiY: 96, PixelFormats.Bgra32, palette: null);
        bmp.WritePixels(new Int32Rect(0, 0, m.Width, m.Height), m.Pixels, m.Stride, 0);
        bmp.Freeze();
        return bmp;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart_dip = e.GetPosition(this);
        _dragCurrent_dip = _dragStart_dip;
        CaptureMouse();
        UpdateSelectionVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _dragCurrent_dip = e.GetPosition(this);
        UpdateSelectionVisual();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        var rect_dip = NormalizeRect(_dragStart_dip, _dragCurrent_dip);
        // Require a minimum drag distance to avoid treating an accidental
        // click as a zero-area "selection". 5×5 DIPs is enough to be deliberate.
        if (rect_dip.Width < 5 || rect_dip.Height < 5)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            return;
        }

        var cropResult = GetSelectionInPixels(rect_dip);
        Completed?.Invoke(this, new SelectionCompletedEventArgs(
            this,
            cropResult.Pixels,
            cropResult.Width_px,
            cropResult.Height_px,
            cropResult.Stride,
            rect_dip));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Rect NormalizeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        return new Rect(x, y, w, h);
    }

    private void UpdateSelectionVisual()
    {
        var rect_dip = NormalizeRect(_dragStart_dip, _dragCurrent_dip);
        if (rect_dip.Width < 1 || rect_dip.Height < 1)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, rect_dip.X);
        Canvas.SetTop(SelectionBorder, rect_dip.Y);
        SelectionBorder.Width = rect_dip.Width;
        SelectionBorder.Height = rect_dip.Height;

        // The ImageBrush viewbox is in absolute coordinates of the source
        // bitmap. The bitmap is in PIXEL space, but the selection rect is
        // in DIP space — convert via DpiScale so we sample the right
        // portion of the source.
        var scale = _monitor.DpiScale;
        var rect_px = new Rect(rect_dip.X * scale, rect_dip.Y * scale,
                               rect_dip.Width * scale, rect_dip.Height * scale);
        ((ImageBrush)SelectionFill.Fill).Viewbox = rect_px;
    }

    // Converts a DIP selection rect (window-local) to a physical-pixel crop
    // of the original capture buffer. This is the ONLY place DIP → PX
    // conversion happens; every other coordinate stays in DIPs.
    private CropResult GetSelectionInPixels(Rect rect_dip)
    {
        var scale = _monitor.DpiScale;
        var x_px = (int)Math.Round(rect_dip.X * scale);
        var y_px = (int)Math.Round(rect_dip.Y * scale);
        var w_px = (int)Math.Round(rect_dip.Width * scale);
        var h_px = (int)Math.Round(rect_dip.Height * scale);

        // Clamp to the source buffer in case rounding pushed us a pixel off.
        x_px = Math.Max(0, Math.Min(x_px, _monitor.Width - 1));
        y_px = Math.Max(0, Math.Min(y_px, _monitor.Height - 1));
        w_px = Math.Min(w_px, _monitor.Width - x_px);
        h_px = Math.Min(h_px, _monitor.Height - y_px);

        var dstStride = w_px * 4;
        var dst = new byte[dstStride * h_px];
        for (int row = 0; row < h_px; row++)
        {
            var srcOffset = (y_px + row) * _monitor.Stride + x_px * 4;
            Buffer.BlockCopy(_monitor.Pixels, srcOffset, dst, row * dstStride, dstStride);
        }
        return new CropResult(dst, w_px, h_px, dstStride);
    }

    // Plays a brief colored flash on the selection border, then closes the
    // window. Used by SnipSession to signal success (green) or no-QR (red).
    public void FlashAndClose(Color color, TimeSpan duration)
    {
        SelectionBorder.BorderBrush = new SolidColorBrush(color);
        SelectionBorder.BorderThickness = new Thickness(4);
        // Schedule the close after the flash duration on the dispatcher.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }

    private sealed record CropResult(byte[] Pixels, int Width_px, int Height_px, int Stride);
}

public sealed class SelectionCompletedEventArgs : EventArgs
{
    public OverlayWindow Source { get; }
    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public Rect SelectionDip { get; }

    public SelectionCompletedEventArgs(
        OverlayWindow source, byte[] pixels, int width, int height, int stride, Rect selectionDip)
    {
        Source = source;
        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        SelectionDip = selectionDip;
    }
}
