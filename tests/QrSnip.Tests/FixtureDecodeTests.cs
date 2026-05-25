using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using QrSnip.Decoding;
using QrSnip.Decoding.Preprocessors;
using Xunit;
using Xunit.Abstractions;

namespace QrSnip.Tests;

// Fixture-driven decode-rate harness for IQrDecoder.
//
// Looks in ./fixtures/ for every supported image, decodes each, and reports
// the results as a markdown table to the test output. Behavior:
//
//   - Every fixture is informational by default — found-count and payloads
//     are reported but don't fail the test.
//   - A fixture with an adjacent <name>.expected.txt becomes a hard
//     assertion: the decoded payload must match the file contents exactly.
//     (For multi-code fixtures, expected.txt has one payload per line.)
//   - Adding a new fixture is a drop-in: no test code changes required.
//
// This lets the suite double as both a regression guard (assertions) AND a
// continuous measurement (the markdown table) so we can watch decode rate
// trend as we add real-world samples.
public sealed class FixtureDecodeTests
{
    private readonly ITestOutputHelper _output;

    public FixtureDecodeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void All_fixtures_decode_or_are_reported()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        Assert.True(Directory.Exists(fixturesDir),
            $"Fixtures directory not found at {fixturesDir}. Did the csproj's None Include copy them?");

        var images = Directory.EnumerateFiles(fixturesDir)
            .Where(p => IsSupportedImage(p))
            .OrderBy(p => p)
            .ToList();

        Assert.NotEmpty(images);

        // Same composition as App.xaml.cs uses in production: ZXingCpp first,
        // then the preprocessor ladder for difficult inputs.
        var decoder = new PreprocessingQrDecoder(
            new ZXingCppQrDecoder(),
            DefaultPreprocessorLadder.Build());
        var rows = new List<Row>();
        var hardFailures = new List<string>();

        foreach (var path in images)
        {
            var name = Path.GetFileName(path);
            var sw = Stopwatch.StartNew();
            IReadOnlyList<QrResult> results;
            try
            {
                var (bgra, w, h, stride) = LoadBgra(path);
                results = decoder.Decode(bgra, w, h, stride);
            }
            catch (Exception ex)
            {
                rows.Add(new Row(name, Found:-1, Payloads:ex.Message, ElapsedMs:sw.ElapsedMilliseconds));
                continue;
            }
            sw.Stop();

            var payloadJoined = results.Count == 0
                ? "(none)"
                : string.Join(" | ", results.Select(r => Truncate(r.Payload, 40)));

            rows.Add(new Row(name, Found:results.Count, Payloads:payloadJoined, ElapsedMs:sw.ElapsedMilliseconds));

            // Hard assertion if there's an expected.txt.
            var expectedPath = Path.Combine(fixturesDir, $"{Path.GetFileNameWithoutExtension(path)}.expected.txt");
            if (File.Exists(expectedPath))
            {
                var expectedLines = File.ReadAllLines(expectedPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                var actualPayloads = results.Select(r => r.Payload).ToList();
                if (!expectedLines.OrderBy(s => s).SequenceEqual(actualPayloads.OrderBy(s => s)))
                {
                    hardFailures.Add(
                        $"{name}: expected [{string.Join(", ", expectedLines)}], " +
                        $"got [{string.Join(", ", actualPayloads)}]");
                }
            }
        }

        EmitReport(rows);

        Assert.True(hardFailures.Count == 0,
            "Fixture mismatches:\n" + string.Join("\n", hardFailures));
    }

    private void EmitReport(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("| Fixture | Found | Time (ms) | Payloads |");
        sb.AppendLine("|---|---:|---:|---|");
        foreach (var r in rows)
        {
            var found = r.Found < 0 ? "ERR" : r.Found.ToString();
            sb.AppendLine($"| {r.Name} | {found} | {r.ElapsedMs} | {r.Payloads} |");
        }
        var decodeRate = rows.Count == 0 ? 0 : (double)rows.Count(r => r.Found > 0) / rows.Count;
        sb.AppendLine();
        sb.AppendLine($"Decode rate: {decodeRate:P0} ({rows.Count(r => r.Found > 0)}/{rows.Count})");
        _output.WriteLine(sb.ToString());
    }

    private static bool IsSupportedImage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" => true,
        _ => false,
    };

    // Loads any image file into a tightly-packed BGRA buffer matching the
    // shape the decoder expects. Stride == width * 4 by construction.
    private static (byte[] bgra, int width, int height, int stride) LoadBgra(string path)
    {
        using var src = new Bitmap(path);
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        using var copy = src.Clone(rect, PixelFormat.Format32bppArgb);
        var bits = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // GDI+ Format32bppArgb is actually BGRA byte order in memory; same
            // layout the decoder reads. Stride may include row padding so we
            // copy row-by-row into a tight buffer to keep the contract simple.
            var width = copy.Width;
            var height = copy.Height;
            var dstStride = width * 4;
            var dst = new byte[dstStride * height];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), dst, y * dstStride, dstStride);
            }
            return (dst, width, height, dstStride);
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

    private sealed record Row(string Name, int Found, string Payloads, long ElapsedMs);
}
