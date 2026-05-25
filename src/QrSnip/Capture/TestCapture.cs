using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace QrSnip.Capture;

// Stage 4 placeholder action: captures every monitor and dumps each as a
// PNG in %TEMP%\qr_snapper_capture\, then opens the folder so we can
// verify visually. Used by both the tray context menu's "Test Capture
// (debug)" item AND the hotkey listener so we can prove end-to-end:
// keypress -> capture -> visible result.
//
// Stage 5 replaces this with: capture -> show overlay on each monitor ->
// user drags selection -> crop -> decode -> clipboard. At that point this
// class becomes the *debug* path only and the hotkey switches to the real
// snip workflow.
internal static class TestCapture
{
    public static async Task RunAsync()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "qr_snapper_capture");
        Directory.CreateDirectory(outDir);
        Diagnostics.LogVerbose($"TestCapture: starting, output dir = {outDir}");
        try
        {
            var capture = new WgcScreenCapture();
            var monitors = await capture.CaptureAllMonitorsAsync();
            for (int i = 0; i < monitors.Length; i++)
            {
                var path = Path.Combine(outDir, $"monitor_{i}.png");
                SaveBgraAsPng(monitors[i], path);
                Diagnostics.LogVerbose($"TestCapture: wrote {path} ({monitors[i].Width}x{monitors[i].Height})");
            }
            Process.Start(new ProcessStartInfo("explorer.exe", outDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("TestCapture", ex);
            MessageBox.Show(
                $"Test capture failed:\n\n{ex.GetType().Name}: {ex.Message}\n\nSee startup.log for details.",
                "QrSnip — Test Capture",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void SaveBgraAsPng(CapturedMonitor m, string path)
    {
        using var bmp = new Bitmap(m.Width, m.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, m.Width, m.Height);
        var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < m.Height; y++)
            {
                Marshal.Copy(m.Pixels, y * m.Stride, IntPtr.Add(bits.Scan0, y * bits.Stride), m.Width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
        bmp.Save(path, ImageFormat.Png);
    }
}
