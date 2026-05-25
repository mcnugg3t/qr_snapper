using System.Collections.Generic;

namespace QrSnip.Decoding.Preprocessors;

// Single source of truth for the preprocessor chain used in production AND
// in the fixture-decode harness. Lets both compositions stay in sync — if
// we add a preprocessor here, the fixture harness immediately measures its
// impact without a separate update.
//
// Ordering: cheap-first, then progressively more aggressive. Each entry
// runs in isolation (NOT chained) inside PreprocessingQrDecoder, EXCEPT
// where the entry itself is a ChainedPreprocessor.
//
// Why this list specifically (post Caleb's 2026-05-25 visual review):
//   - Original failed_*.png are blurry, low-contrast, with QR modules ~3-8px
//   - The w=7 adaptive-contrast was too narrow (Caleb: "chaotic pixels")
//   - Sharpen alone helped visually but didn't push ZXing over the line
//   - Combination of sharpen+contrast is the natural next experiment
public static class DefaultPreprocessorLadder
{
    public static IReadOnlyList<IQrPreprocessor> Build() => new IQrPreprocessor[]
    {
        // Single-step transforms, lightest first.
        new SharpenPreprocessor(SharpenPreprocessor.Strength.Mild),
        new SharpenPreprocessor(SharpenPreprocessor.Strength.Strong),
        new AdaptiveContrastPreprocessor(windowRadius: 7),
        new AdaptiveContrastPreprocessor(windowRadius: 25),
        new AdaptiveContrastPreprocessor(windowRadius: 50),
        new UpscalePreprocessor(factor: 3),

        // Combined transforms — sharpen first to crisp edges, then binarize
        // with a window large enough to cover several QR modules.
        new ChainedPreprocessor(
            new SharpenPreprocessor(SharpenPreprocessor.Strength.Strong),
            new AdaptiveContrastPreprocessor(windowRadius: 25)),

        // The kitchen sink: sharpen, then upscale to give the binarizer more
        // pixels per module to work with, then adaptive-contrast.
        new ChainedPreprocessor(
            new SharpenPreprocessor(SharpenPreprocessor.Strength.Strong),
            new UpscalePreprocessor(factor: 3),
            new AdaptiveContrastPreprocessor(windowRadius: 50)),
    };
}
