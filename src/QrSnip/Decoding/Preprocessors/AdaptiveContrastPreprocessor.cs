using System;

namespace QrSnip.Decoding.Preprocessors;

// Adaptive local contrast normalization. For each pixel, compute the mean
// luminance of a window around it, then threshold the pixel against that
// local mean. Dark-on-light regions become solid black; light-on-dark
// regions become solid white. Output is binary.
//
// Window size matters. Too small (window covers only ~1 module) and the
// local mean is dominated by the pixel's own module, so the threshold
// collapses to "all uniform". Too large (window covers many modules) and
// the mean stops tracking the paper background and we get global behavior.
// Caleb (2026-05-25) observed the WindowRadius=7 default produced "chaotic
// pixels at too small a scale" on his fixtures — implying the window was
// covering only a few QR modules. We now ship multiple preprocessors at
// different radii so each fixture has a chance of finding a matching scale.
//
// Constructor parameters:
//   windowRadius: half-width of the square sampling window in pixels.
//                 A good rule of thumb is ~5-10x the QR module size.
//   thresholdRatio: pixels darker than mean*thresholdRatio become 0,
//                   else 255. 0.95 = mild bias toward "dark" classification.
public sealed class AdaptiveContrastPreprocessor : IQrPreprocessor
{
    private readonly int _windowRadius;
    private readonly int _thresholdPercent;

    public AdaptiveContrastPreprocessor(int windowRadius = 7, double thresholdRatio = 0.95)
    {
        if (windowRadius < 1) throw new ArgumentOutOfRangeException(nameof(windowRadius));
        _windowRadius = windowRadius;
        _thresholdPercent = (int)(thresholdRatio * 100);
    }

    public string Name => $"adaptive-contrast-w{_windowRadius}";

    public PreprocessedImage Apply(byte[] bgra, int width, int height, int stride)
    {
        // First pass: build a grayscale luminance plane (one byte per pixel,
        // tightly packed). Working in a separate plane lets us compute
        // window means cheaply and keeps the output strictly black/white.
        var luma = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            var rowSrc = y * stride;
            var rowDst = y * width;
            for (int x = 0; x < width; x++)
            {
                var src = rowSrc + x * 4;
                // ITU-R BT.601 luma — matches the BgraLuminanceSource in
                // ZXingQrDecoder so preprocessing math agrees with decode math.
                luma[rowDst + x] = (byte)((306 * bgra[src + 2] + 601 * bgra[src + 1] + 117 * bgra[src + 0] + 512) >> 10);
            }
        }

        // Second pass: integral image (summed-area table) so we can compute
        // arbitrary-window sums in O(1). Long accumulator because a 4K image
        // can have a full-image sum > 2^31.
        var integral = new long[width * height];
        for (int y = 0; y < height; y++)
        {
            long rowSum = 0;
            for (int x = 0; x < width; x++)
            {
                rowSum += luma[y * width + x];
                integral[y * width + x] = rowSum + (y > 0 ? integral[(y - 1) * width + x] : 0);
            }
        }

        // Third pass: for each pixel, compare to local mean.
        var output = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var mean = LocalMean(integral, x, y, width, height);
                var pixel = luma[y * width + x];
                var threshold = (mean * _thresholdPercent) / 100;
                byte v = pixel < threshold ? (byte)0 : (byte)255;
                var dst = y * stride + x * 4;
                output[dst + 0] = v;
                output[dst + 1] = v;
                output[dst + 2] = v;
                output[dst + 3] = 255;
            }
        }
        return new PreprocessedImage(output, width, height, stride);
    }

    private int LocalMean(long[] integral, int cx, int cy, int width, int height)
    {
        var x0 = Math.Max(0, cx - _windowRadius);
        var y0 = Math.Max(0, cy - _windowRadius);
        var x1 = Math.Min(width - 1, cx + _windowRadius);
        var y1 = Math.Min(height - 1, cy + _windowRadius);

        long sum = integral[y1 * width + x1];
        if (x0 > 0) sum -= integral[y1 * width + (x0 - 1)];
        if (y0 > 0) sum -= integral[(y0 - 1) * width + x1];
        if (x0 > 0 && y0 > 0) sum += integral[(y0 - 1) * width + (x0 - 1)];

        var area = (x1 - x0 + 1) * (y1 - y0 + 1);
        return (int)(sum / area);
    }
}
