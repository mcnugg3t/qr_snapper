namespace QrSnip.Decoding.Preprocessors;

// 3x3 convolution sharpening kernel. Crisps up the edges of finder patterns
// in blurry/soft inputs so ZXing's locator has stronger gradients to detect.
//
// Two strengths:
//   - Mild: kernel = [0,-1,0; -1,5,-1; 0,-1,0]  (sums to 1)
//     Adjacent pixels only. Less aggressive but preserves more of the
//     original image character.
//   - Strong: kernel = [-1,-1,-1; -1,9,-1; -1,-1,-1]  (sums to 1)
//     Includes diagonal neighbors too. Much more aggressive — corners and
//     edges pop dramatically, but can introduce halo artifacts in noisy
//     inputs.
public sealed class SharpenPreprocessor : IQrPreprocessor
{
    public enum Strength { Mild, Strong }

    private readonly Strength _strength;

    public SharpenPreprocessor(Strength strength = Strength.Mild)
    {
        _strength = strength;
    }

    public string Name => $"sharpen-{_strength.ToString().ToLowerInvariant()}";

    public PreprocessedImage Apply(byte[] bgra, int width, int height, int stride)
    {
        var output = new byte[bgra.Length];
        var useStrong = _strength == Strength.Strong;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Copy edges unchanged — they don't have a full 3x3 neighborhood.
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    var srcEdge = y * stride + x * 4;
                    output[srcEdge + 0] = bgra[srcEdge + 0];
                    output[srcEdge + 1] = bgra[srcEdge + 1];
                    output[srcEdge + 2] = bgra[srcEdge + 2];
                    output[srcEdge + 3] = bgra[srcEdge + 3];
                    continue;
                }

                for (int c = 0; c < 3; c++)
                {
                    var center = bgra[y * stride + x * 4 + c];
                    var up     = bgra[(y - 1) * stride + x * 4 + c];
                    var down   = bgra[(y + 1) * stride + x * 4 + c];
                    var left   = bgra[y * stride + (x - 1) * 4 + c];
                    var right  = bgra[y * stride + (x + 1) * 4 + c];

                    int v;
                    if (useStrong)
                    {
                        var ul = bgra[(y - 1) * stride + (x - 1) * 4 + c];
                        var ur = bgra[(y - 1) * stride + (x + 1) * 4 + c];
                        var dl = bgra[(y + 1) * stride + (x - 1) * 4 + c];
                        var dr = bgra[(y + 1) * stride + (x + 1) * 4 + c];
                        v = 9 * center - up - down - left - right - ul - ur - dl - dr;
                    }
                    else
                    {
                        v = 5 * center - up - down - left - right;
                    }

                    if (v < 0) v = 0;
                    else if (v > 255) v = 255;
                    output[y * stride + x * 4 + c] = (byte)v;
                }
                output[y * stride + x * 4 + 3] = bgra[y * stride + x * 4 + 3]; // alpha
            }
        }
        return new PreprocessedImage(output, width, height, stride);
    }
}
