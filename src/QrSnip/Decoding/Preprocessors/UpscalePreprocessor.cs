namespace QrSnip.Decoding.Preprocessors;

// Integer-factor nearest-neighbor upscale. For tiny QRs where each module
// is only 2-3 source pixels, scaling 3x or 4x gives ZXing enough resolution
// to reliably distinguish dark modules from light gaps.
//
// Nearest-neighbor (not bilinear) is deliberate: bilinear would blur the
// module edges we're trying to make ZXing see. Nearest preserves the
// black/white module boundaries crisply.
public sealed class UpscalePreprocessor : IQrPreprocessor
{
    private readonly int _factor;

    public UpscalePreprocessor(int factor)
    {
        if (factor < 2) throw new System.ArgumentOutOfRangeException(nameof(factor), "Upscale factor must be >= 2");
        _factor = factor;
    }

    public string Name => $"upscale-{_factor}x";

    public PreprocessedImage Apply(byte[] bgra, int width, int height, int stride)
    {
        var dstWidth = width * _factor;
        var dstHeight = height * _factor;
        var dstStride = dstWidth * 4;
        var dst = new byte[dstStride * dstHeight];

        for (int y = 0; y < dstHeight; y++)
        {
            var srcY = y / _factor;
            for (int x = 0; x < dstWidth; x++)
            {
                var srcX = x / _factor;
                var srcI = srcY * stride + srcX * 4;
                var dstI = y * dstStride + x * 4;
                dst[dstI + 0] = bgra[srcI + 0];
                dst[dstI + 1] = bgra[srcI + 1];
                dst[dstI + 2] = bgra[srcI + 2];
                dst[dstI + 3] = bgra[srcI + 3];
            }
        }
        return new PreprocessedImage(dst, dstWidth, dstHeight, dstStride);
    }
}
