using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using QrSnip.Decoding;

namespace DecoderViewer;

// Visualizes what the QR decoder sees. Takes one or more image paths,
// decodes each, and writes a side-by-side annotated PNG to %TEMP% showing:
//   - the original image
//   - any detected QR bounding boxes drawn in green
//   - the decoded payload(s) printed below
//
// Useful when "the decoder said no" and you want to know why — is the QR
// too small? too low-contrast? being detected but mis-decoded? Open the
// annotated PNG and you can usually tell at a glance.
//
// Usage:
//   dotnet run --project tools/DecoderViewer -- path/to/image.png [more...]
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: DecoderViewer <image-path> [more...]");
            Console.Error.WriteLine("Tip: pass a directory and we'll process every supported image inside.");
            return 1;
        }

        var inputs = ExpandInputs(args);
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("No supported images found in the given inputs.");
            return 1;
        }

        var outDir = Path.Combine(Path.GetTempPath(), "qr_snapper_decoder_view");
        Directory.CreateDirectory(outDir);

        var decoder = new ZXingQrDecoder();
        var firstOutput = string.Empty;
        foreach (var path in inputs)
        {
            var sw = Stopwatch.StartNew();
            var (pixels, w, h, stride) = LoadBgra(path);
            var results = decoder.Decode(pixels, w, h, stride);
            sw.Stop();

            var outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".decoded.png");
            WriteAnnotated(path, results, sw.ElapsedMilliseconds, outPath);
            firstOutput = firstOutput == string.Empty ? outPath : firstOutput;

            Console.WriteLine($"{Path.GetFileName(path)}: found {results.Count} in {sw.ElapsedMilliseconds}ms");
            foreach (var r in results)
            {
                Console.WriteLine($"  -> {Truncate(r.Payload, 80)}");
            }
            Console.WriteLine($"  annotated: {outPath}");
        }

        // Open the folder so the user can see all annotated outputs.
        if (firstOutput != string.Empty)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", outDir) { UseShellExecute = true });
        }
        return 0;
    }

    private static List<string> ExpandInputs(string[] args)
    {
        var inputs = new List<string>();
        foreach (var arg in args)
        {
            if (Directory.Exists(arg))
            {
                inputs.AddRange(Directory.EnumerateFiles(arg).Where(IsSupportedImage));
            }
            else if (File.Exists(arg) && IsSupportedImage(arg))
            {
                inputs.Add(arg);
            }
            else
            {
                Console.Error.WriteLine($"Skipping (not found or unsupported): {arg}");
            }
        }
        return inputs;
    }

    private static bool IsSupportedImage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" => true,
        _ => false,
    };

    private static (byte[] pixels, int w, int h, int stride) LoadBgra(string path)
    {
        using var src = new Bitmap(path);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        using var copy = src.Clone(rect, PixelFormat.Format32bppArgb);
        var bits = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var dstStride = copy.Width * 4;
            var dst = new byte[dstStride * copy.Height];
            for (int y = 0; y < copy.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), dst, y * dstStride, dstStride);
            }
            return (dst, copy.Width, copy.Height, dstStride);
        }
        finally
        {
            copy.UnlockBits(bits);
        }
    }

    private static void WriteAnnotated(string sourcePath, IReadOnlyList<QrResult> results, long elapsedMs, string outPath)
    {
        using var src = Image.FromFile(sourcePath);

        const int captionHeight = 80;
        using var annotated = new Bitmap(src.Width, src.Height + captionHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(annotated);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw the source image at the top.
        g.DrawImage(src, 0, 0, src.Width, src.Height);

        // Draw any detected QR bounding boxes in lime green.
        using var boxPen = new Pen(Color.LimeGreen, 3);
        foreach (var r in results)
        {
            var b = r.Box;
            if (b.Width > 0 && b.Height > 0)
            {
                g.DrawRectangle(boxPen, b.X, b.Y, b.Width, b.Height);
            }
        }

        // Caption area: white background, status text on top.
        g.FillRectangle(Brushes.White, 0, src.Height, src.Width, captionHeight);
        var statusText = results.Count == 0
            ? $"NO QR DETECTED ({elapsedMs}ms)"
            : $"FOUND {results.Count} in {elapsedMs}ms:";
        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 10);
        var titleColor = results.Count == 0 ? Brushes.Red : Brushes.DarkGreen;
        g.DrawString(statusText, titleFont, titleColor, 8, src.Height + 4);

        var y = src.Height + 28;
        foreach (var r in results.Take(3))
        {
            g.DrawString(Truncate(r.Payload, 90), bodyFont, Brushes.Black, 8, y);
            y += 18;
        }

        annotated.Save(outPath, ImageFormat.Png);
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s.Substring(0, max - 1) + "…";
    }
}
