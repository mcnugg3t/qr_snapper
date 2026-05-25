using System.Collections.Generic;

namespace QrSnip.Decoding;

// IQrDecoder that wraps an inner decoder (ZXingQrDecoder) and tries a chain
// of preprocessors as additional fallback attempts. Each preprocessor is
// applied to the ORIGINAL raw pixels (not chained), so preprocessors stay
// independent and we don't compound their effects.
//
// Decode order:
//   1. Inner decoder on raw pixels.       (cheap, handles clean inputs)
//   2. For each preprocessor: apply, try inner decoder on output.
//   3. Return the first non-empty result, or empty if all fail.
//
// Performance: preprocessors only run when needed (step 1 succeeded for the
// easy case). On a fully-clean QR the cost is the same as raw ZXingQrDecoder.
// On a hard input we pay ~10-50ms per preprocessor.
public sealed class PreprocessingQrDecoder : IQrDecoder
{
    private readonly IQrDecoder _inner;
    private readonly IReadOnlyList<IQrPreprocessor> _preprocessors;

    public PreprocessingQrDecoder(IQrDecoder inner, IReadOnlyList<IQrPreprocessor> preprocessors)
    {
        _inner = inner;
        _preprocessors = preprocessors;
    }

    public IReadOnlyList<QrResult> Decode(
        System.ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var direct = _inner.Decode(bgra, width, height, stride);
        if (direct.Count > 0)
        {
            Diagnostics.LogVerbose("PreprocessingQrDecoder: decoded directly");
            return direct;
        }

        // Each preprocessor sees the original input. ReadOnlySpan can't cross
        // the call boundary into Apply (it takes byte[]), so we materialize once.
        var raw = bgra.ToArray();
        foreach (var p in _preprocessors)
        {
            var preprocessed = p.Apply(raw, width, height, stride);
            var result = _inner.Decode(
                preprocessed.Bgra,
                preprocessed.Width,
                preprocessed.Height,
                preprocessed.Stride);
            if (result.Count > 0)
            {
                Diagnostics.LogVerbose($"PreprocessingQrDecoder: decoded after preprocessor '{p.Name}'");
                return result;
            }
        }
        Diagnostics.LogVerbose("PreprocessingQrDecoder: no result after all preprocessors");
        return System.Array.Empty<QrResult>();
    }
}
