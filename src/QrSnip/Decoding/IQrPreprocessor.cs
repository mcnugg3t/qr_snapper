namespace QrSnip.Decoding;

// A pixel-transformation pass tried as an additional decode attempt when
// the raw image doesn't decode.
//
// PreprocessingQrDecoder applies these as additional fallback attempts AFTER
// the existing ZXingQrDecoder ladder fails — so the easy case (clean QR)
// stays cheap and only difficult inputs pay the preprocessing cost.
//
// Why an interface (vs. e.g. a Func<byte[], int, int, int, PreprocessedImage>):
//   - Each preprocessor needs a name for diagnostic logging ("decoded after
//     SauvolaBinarizer pass") and we want that name to survive serialization
//     to the fixture-harness report.
//   - Several preprocessors will have configurable parameters in the future
//     (sharpen strength, contrast window size) — instance state fits cleaner
//     than closures.
public interface IQrPreprocessor
{
    // Short identifier for logs and reports, e.g. "sharpen-3x3" or "sauvola-w15".
    string Name { get; }

    // Apply the transformation. Returns a new buffer (preprocessors don't
    // mutate input). Width/height may differ from input (e.g. upscaling).
    PreprocessedImage Apply(byte[] bgra, int width, int height, int stride);
}

public sealed record PreprocessedImage(byte[] Bgra, int Width, int Height, int Stride);
