using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QrSnip.Capture;
using QrSnip.Decoding;
using Xunit;

namespace QrSnip.Tests;

// End-to-end test of the FakeScreenCapture → IQrDecoder pipeline.
//
// This validates the IScreenCapture contract is consumable from another
// component (the decoder) WITHOUT requiring a real WGC capture. When Stage 5
// adds the overlay and crop logic, that same path will be testable against
// FakeScreenCapture the same way.
//
// What we're checking: a fixture PNG can be loaded as if it were a captured
// monitor, its pixel buffer extracted, and handed to the decoder, and the
// decoder still returns the expected payload. This catches regressions in
// the BGRA buffer layout (stride, pixel order) that the standalone fixture
// decode test wouldn't.
public sealed class CaptureToDecodeIntegrationTests
{
    [Fact]
    public async Task FakeCapture_pipes_pixels_to_decoder_intact()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var cleanQrPath = Path.Combine(fixturesDir, "clean_qr.png");
        Assert.True(File.Exists(cleanQrPath), $"Missing fixture {cleanQrPath}");

        var capture = new FakeScreenCapture(cleanQrPath);
        var monitors = await capture.CaptureAllMonitorsAsync();

        Assert.Single(monitors);
        var m = monitors[0];

        var decoder = new ZXingQrDecoder();
        var results = decoder.Decode(m.Pixels, m.Width, m.Height, m.Stride);

        Assert.Single(results);
        Assert.Equal("hello qr_snapper", results[0].Payload);
    }

    [Fact]
    public async Task FakeCapture_multiple_monitors_arranged_horizontally()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var paths = new[]
        {
            Path.Combine(fixturesDir, "clean_qr.png"),
            Path.Combine(fixturesDir, "small_qr.png"),
        };

        var capture = new FakeScreenCapture(paths);
        var monitors = await capture.CaptureAllMonitorsAsync();

        Assert.Equal(2, monitors.Length);
        Assert.Equal(0, monitors[0].DesktopX);
        Assert.Equal(monitors[0].Width, monitors[1].DesktopX);
        Assert.All(monitors, m => Assert.Equal(0, m.DesktopY));
    }
}
