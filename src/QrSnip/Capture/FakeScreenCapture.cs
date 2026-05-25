using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace QrSnip.Capture;

// Fake IScreenCapture for tests. Returns one CapturedMonitor per image path
// it was constructed with, loaded from disk as BGRA. Lets us exercise the
// overlay → crop → decode pipeline against fixture PNGs without WGC.
//
// Why this lives in the production assembly rather than the test project:
// FakeScreenCapture is also useful for manual UI testing (point the app at
// fixture PNGs to develop the overlay without snipping the real desktop).
// If the surface area grows beyond "load from disk" we'll move it.
public sealed class FakeScreenCapture : IScreenCapture
{
    private readonly string[] _paths;

    public FakeScreenCapture(params string[] imagePaths)
    {
        _paths = imagePaths ?? throw new ArgumentNullException(nameof(imagePaths));
    }

    public Task<CapturedMonitor[]> CaptureAllMonitorsAsync()
    {
        var monitors = new CapturedMonitor[_paths.Length];
        var xOffset = 0;
        for (int i = 0; i < _paths.Length; i++)
        {
            var m = LoadAsMonitor(_paths[i], desktopX: xOffset, desktopY: 0);
            monitors[i] = m;
            xOffset += m.Width;
        }
        return Task.FromResult(monitors);
    }

    private static CapturedMonitor LoadAsMonitor(string path, int desktopX, int desktopY)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture image not found: {path}");

        using var src = new Bitmap(path);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        using var copy = src.Clone(rect, PixelFormat.Format32bppArgb);
        var bits = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var width = copy.Width;
            var height = copy.Height;
            var dstStride = width * 4;
            var dst = new byte[dstStride * height];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), dst, y * dstStride, dstStride);
            }
            return new CapturedMonitor(
                DesktopX: desktopX,
                DesktopY: desktopY,
                Width: width,
                Height: height,
                Stride: dstStride,
                Pixels: dst,
                DpiScale: 1.0);
        }
        finally
        {
            copy.UnlockBits(bits);
        }
    }
}
