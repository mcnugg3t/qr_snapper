using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;

namespace FixtureGen;

// One-shot generator for the synthetic QR fixtures used by FixtureDecodeTests.
// Run any time the synthetic fixture set should be regenerated. Output PNGs
// are committed to the repo so tests don't depend on this tool at run time.
//
// We ship three synthetic fixtures to bootstrap the harness:
//   - clean_qr.png       — 300x300, perfect render. Baseline.
//   - small_qr.png       — 80x80, tests pixel-density floor.
//   - rotated_qr.png     — 300x300, rotated 7 deg, tests in-plane rotation.
//
// Each fixture's expected payload is written to <name>.expected.txt alongside.
internal static class Program
{
    private const string OutputDir = "tests/QrSnip.Tests/fixtures";
    private const string Payload = "hello qr_snapper";

    private static int Main()
    {
        Directory.CreateDirectory(OutputDir);

        WriteFixture("clean_qr", Payload, size: 300, rotateDegrees: 0);
        WriteFixture("small_qr", Payload, size: 80, rotateDegrees: 0);
        WriteFixture("rotated_qr", Payload, size: 300, rotateDegrees: 7);

        Console.WriteLine($"Wrote 3 fixtures to {OutputDir}/");
        return 0;
    }

    private static void WriteFixture(string name, string payload, int size, double rotateDegrees)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = size,
                Height = size,
                Margin = 4,
                ErrorCorrection = ErrorCorrectionLevel.M,
            },
        };

        var pixelData = writer.Write(payload);
        using var bmp = PixelsToBitmap(pixelData);
        using var final = rotateDegrees == 0 ? (Bitmap)bmp.Clone() : RotateBitmap(bmp, rotateDegrees);

        var pngPath = Path.Combine(OutputDir, $"{name}.png");
        final.Save(pngPath, ImageFormat.Png);

        var expectedPath = Path.Combine(OutputDir, $"{name}.expected.txt");
        File.WriteAllText(expectedPath, payload);

        Console.WriteLine($"  wrote {pngPath} ({final.Width}x{final.Height}) + {Path.GetFileName(expectedPath)}");
    }

    private static Bitmap PixelsToBitmap(PixelData pixels)
    {
        var bmp = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, pixels.Width, pixels.Height);
        var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(pixels.Pixels, 0, bits.Scan0, pixels.Pixels.Length);
        bmp.UnlockBits(bits);
        return bmp;
    }

    private static Bitmap RotateBitmap(Bitmap source, double degrees)
    {
        // Rotate around center on a white canvas large enough to fit the
        // rotated image without clipping. Real scans of paper would have a
        // white background, so this matches the realistic case.
        var rotated = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(rotated);
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TranslateTransform(source.Width / 2f, source.Height / 2f);
        g.RotateTransform((float)degrees);
        g.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
        g.DrawImage(source, 0, 0);
        return rotated;
    }
}
