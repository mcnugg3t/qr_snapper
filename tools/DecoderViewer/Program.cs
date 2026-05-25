using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using QrSnip.Decoding;
using QrSnip.Decoding.Preprocessors;

namespace DecoderViewer;

// Visualizes what the QR decoder pipeline sees. For each input image:
//   1. Runs the raw ZXingQrDecoder on the original.
//   2. Runs each IQrPreprocessor + ZXingQrDecoder on the preprocessor output.
//   3. Saves an annotated PNG per attempt, plus a combined .summary.png.
//
// Output goes to %TEMP%\qr_snapper_decoder_view\<input-name>\, one folder
// per input. The summary PNG shows the original + each preprocessor's
// output side-by-side with green/red captions for decoded/failed.
//
// Useful when "the decoder said no" and you want to know WHY — open the
// summary PNG and you can see whether (e.g.) adaptive contrast actually
// made the QR visible vs. just made the noise more visible.
//
// Usage:
//   dotnet run --project tools/DecoderViewer -- path/to/image.png [more...]
//   dotnet run --project tools/DecoderViewer -- path/to/dir/   (processes every supported image)
internal static class Program
{
    private record Stage(string Name, byte[] Bgra, int Width, int Height, int Stride, IReadOnlyList<QrResult> Results, long ElapsedMs);

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

        var outRoot = Path.Combine(Path.GetTempPath(), "qr_snapper_decoder_view");
        // Clean previous output so old fixtures don't linger and confuse us.
        if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        Directory.CreateDirectory(outRoot);

        var decoder = new ZXingCppQrDecoder();
        var preprocessors = DefaultPreprocessorLadder.Build();

        foreach (var path in inputs)
        {
            ProcessOne(path, decoder, preprocessors, outRoot);
        }

        Process.Start(new ProcessStartInfo("explorer.exe", outRoot) { UseShellExecute = true });
        return 0;
    }

    private static void ProcessOne(string path, IQrDecoder decoder, IReadOnlyList<IQrPreprocessor> preprocessors, string outRoot)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var fixtureDir = Path.Combine(outRoot, name);
        Directory.CreateDirectory(fixtureDir);

        var (bgra, w, h, stride) = LoadBgra(path);
        var stages = new List<Stage>();

        // Stage 0: raw original.
        var sw = Stopwatch.StartNew();
        var rawResults = decoder.Decode(bgra, w, h, stride);
        sw.Stop();
        stages.Add(new Stage("original", bgra, w, h, stride, rawResults, sw.ElapsedMilliseconds));

        // Stages 1+: each preprocessor in isolation. (Each preprocessor sees
        // the raw input, NOT the previous stage's output — matches what
        // PreprocessingQrDecoder does at runtime.)
        foreach (var p in preprocessors)
        {
            sw.Restart();
            var processed = p.Apply(bgra, w, h, stride);
            var results = decoder.Decode(processed.Bgra, processed.Width, processed.Height, processed.Stride);
            sw.Stop();
            stages.Add(new Stage(p.Name, processed.Bgra, processed.Width, processed.Height, processed.Stride, results, sw.ElapsedMilliseconds));
        }

        // Per-stage annotated PNGs.
        foreach (var s in stages)
        {
            var stagePath = Path.Combine(fixtureDir, $"{s.Name}.png");
            WriteAnnotatedStage(s, stagePath);
        }

        // Console summary line.
        var firstWin = stages.FirstOrDefault(s => s.Results.Count > 0);
        if (firstWin is null)
        {
            Console.WriteLine($"{Path.GetFileName(path)}: NO STAGE DECODED ({stages.Count} attempts)");
        }
        else
        {
            Console.WriteLine($"{Path.GetFileName(path)}: decoded by '{firstWin.Name}' ({firstWin.ElapsedMs}ms) -> {Truncate(firstWin.Results[0].Payload, 80)}");
        }
    }

    private static void WriteAnnotatedStage(Stage stage, string outPath)
    {
        const int captionHeight = 60;
        using var bmp = BgraToBitmap(stage.Bgra, stage.Width, stage.Height, stage.Stride);
        using var annotated = new Bitmap(stage.Width, stage.Height + captionHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(annotated);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        g.DrawImage(bmp, 0, 0, stage.Width, stage.Height);

        using var boxPen = new Pen(Color.LimeGreen, 3);
        foreach (var r in stage.Results)
        {
            var b = r.Box;
            if (b.Width > 0 && b.Height > 0)
            {
                g.DrawRectangle(boxPen, b.X, b.Y, b.Width, b.Height);
            }
        }

        g.FillRectangle(Brushes.White, 0, stage.Height, stage.Width, captionHeight);
        var statusText = stage.Results.Count == 0
            ? $"[{stage.Name}] NO QR ({stage.ElapsedMs}ms)"
            : $"[{stage.Name}] FOUND {stage.Results.Count} in {stage.ElapsedMs}ms";
        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 10);
        var titleColor = stage.Results.Count == 0 ? Brushes.Red : Brushes.DarkGreen;
        g.DrawString(statusText, titleFont, titleColor, 8, stage.Height + 4);

        if (stage.Results.Count > 0)
        {
            g.DrawString(Truncate(stage.Results[0].Payload, 90), bodyFont, Brushes.Black, 8, stage.Height + 28);
        }

        annotated.Save(outPath, ImageFormat.Png);
    }

    private static Bitmap BgraToBitmap(byte[] bgra, int width, int height, int stride)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(bgra, y * stride, IntPtr.Add(bits.Scan0, y * bits.Stride), width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
        return bmp;
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

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s.Substring(0, max - 1) + "…";
    }
}
