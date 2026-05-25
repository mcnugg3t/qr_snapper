using System.Linq;

namespace QrSnip.Decoding.Preprocessors;

// Composes multiple preprocessors into a pipeline: A's output feeds B, etc.
//
// Useful when no single preprocessor is enough but a sequence works — e.g.
// sharpen-the-edges then adaptive-contrast turns a blurry low-contrast scan
// into a crisp binary image, even though neither preprocessor alone makes
// it readable.
//
// Composition order matters. Sharpen-then-binarize is good (sharper edges
// give the threshold cleaner boundaries). Binarize-then-sharpen is bad
// (sharpening a binary image just produces noise on edges).
public sealed class ChainedPreprocessor : IQrPreprocessor
{
    private readonly IQrPreprocessor[] _stages;

    public ChainedPreprocessor(params IQrPreprocessor[] stages)
    {
        _stages = stages;
    }

    // Underscores rather than colons so the name is filesystem-safe — the
    // DecoderViewer writes per-stage PNGs using the Name as a filename, and
    // a colon would be parsed as a Windows drive separator (silently
    // creating a directory instead of a file). Found 2026-05-25 when colon
    // filenames mangled the DecoderViewer output.
    public string Name => "chain_" + string.Join("+", _stages.Select(s => s.Name));

    public PreprocessedImage Apply(byte[] bgra, int width, int height, int stride)
    {
        var current = new PreprocessedImage(bgra, width, height, stride);
        foreach (var stage in _stages)
        {
            current = stage.Apply(current.Bgra, current.Width, current.Height, current.Stride);
        }
        return current;
    }
}
