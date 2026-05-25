using System;
using System.Collections.Generic;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;
using ZXing.QrCode;

namespace QrSnip.Decoding;

// ZXing-backed QR decoder with a small fallback ladder.
//
// Decode pipeline:
//   1. Try multi-decode with HybridBinarizer + TRY_HARDER. This handles the
//      90%+ case (clean snip, one-or-more QRs, normal contrast).
//   2. On no-result, retry with GlobalHistogramBinarizer. The two binarizers
//      handle different lighting regimes; one often succeeds where the other
//      fails on low-contrast images.
//   3. On still-no-result, upscale 2x nearest-neighbor and retry pipeline.
//      Helps small-pixel-density QRs (scanned at low resolution or from
//      compressed sources).
//   4. Still nothing: return empty list.
//
// Each step adds ~5-20ms only on failure. The fixture harness will tell us
// empirically whether steps 2 and 3 pay for themselves on real samples.
public sealed class ZXingQrDecoder : IQrDecoder
{
    public IReadOnlyList<QrResult> Decode(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int stride)
    {
        // ReadOnlySpan can't cross the lambda/closure boundary we need for
        // ZXing's LuminanceSource. Copy once into a managed array. For the
        // sizes we deal with (a snipped region, not a whole monitor) this is
        // a few hundred KB at worst.
        var pixels = bgra.ToArray();

        var result = TryDecode(pixels, width, height, stride, useHybrid: true);
        if (result.Count > 0) return result;

        result = TryDecode(pixels, width, height, stride, useHybrid: false);
        if (result.Count > 0) return result;

        // Upscale 2x nearest-neighbor and retry the primary path. We do this
        // last because upscaling doubles the pixel count, which roughly
        // quadruples decode time on the retry.
        var (upscaled, upWidth, upHeight, upStride) = Upscale2xNearestNeighbor(pixels, width, height, stride);
        result = TryDecode(upscaled, upWidth, upHeight, upStride, useHybrid: true);
        return ScaleBoundingBoxes(result, scale: 0.5);
    }

    private static IReadOnlyList<QrResult> TryDecode(
        byte[] bgra, int width, int height, int stride, bool useHybrid)
    {
        var luminance = new BgraLuminanceSource(bgra, width, height, stride);
        var binarizer = useHybrid ? new HybridBinarizer(luminance) : (Binarizer)new GlobalHistogramBinarizer(luminance);
        var bitmap = new BinaryBitmap(binarizer);

        var hints = new Dictionary<DecodeHintType, object>
        {
            [DecodeHintType.TRY_HARDER] = true,
            [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
        };

        // Multi-decode: a single snip may contain several QRs. The QRCodeMultiReader
        // handles this directly without the GenericMultipleBarcodeReader's
        // overhead of trying every format.
        var reader = new QRCodeMultiReader();
        Result[]? results;
        try
        {
            results = reader.decodeMultiple(bitmap, hints);
        }
        catch (ReaderException)
        {
            // ZXing's "not found" path. Not an error condition for us.
            return Array.Empty<QrResult>();
        }

        if (results is null || results.Length == 0) return Array.Empty<QrResult>();

        var list = new List<QrResult>(results.Length);
        foreach (var r in results)
        {
            list.Add(new QrResult(r.Text, ComputeBoundingBox(r.ResultPoints)));
        }
        return list;
    }

    private static BoundingBox ComputeBoundingBox(ResultPoint[]? points)
    {
        if (points is null || points.Length == 0) return new BoundingBox(0, 0, 0, 0);

        float minX = points[0].X, minY = points[0].Y;
        float maxX = points[0].X, maxY = points[0].Y;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i].X < minX) minX = points[i].X;
            if (points[i].Y < minY) minY = points[i].Y;
            if (points[i].X > maxX) maxX = points[i].X;
            if (points[i].Y > maxY) maxY = points[i].Y;
        }
        return new BoundingBox(
            (int)minX, (int)minY,
            (int)(maxX - minX), (int)(maxY - minY));
    }

    private static IReadOnlyList<QrResult> ScaleBoundingBoxes(IReadOnlyList<QrResult> results, double scale)
    {
        if (results.Count == 0 || scale == 1.0) return results;
        var scaled = new List<QrResult>(results.Count);
        foreach (var r in results)
        {
            var b = r.Box;
            scaled.Add(r with { Box = new BoundingBox(
                (int)(b.X * scale),
                (int)(b.Y * scale),
                (int)(b.Width * scale),
                (int)(b.Height * scale)) });
        }
        return scaled;
    }

    private static (byte[] pixels, int width, int height, int stride) Upscale2xNearestNeighbor(
        byte[] src, int srcWidth, int srcHeight, int srcStride)
    {
        var dstWidth = srcWidth * 2;
        var dstHeight = srcHeight * 2;
        var dstStride = dstWidth * 4;
        var dst = new byte[dstStride * dstHeight];

        for (int y = 0; y < dstHeight; y++)
        {
            var srcY = y / 2;
            for (int x = 0; x < dstWidth; x++)
            {
                var srcX = x / 2;
                var srcIndex = srcY * srcStride + srcX * 4;
                var dstIndex = y * dstStride + x * 4;
                dst[dstIndex + 0] = src[srcIndex + 0];
                dst[dstIndex + 1] = src[srcIndex + 1];
                dst[dstIndex + 2] = src[srcIndex + 2];
                dst[dstIndex + 3] = src[srcIndex + 3];
            }
        }
        return (dst, dstWidth, dstHeight, dstStride);
    }
}

// LuminanceSource that reads BGRA pixels directly. ZXing ships RGBLuminanceSource
// but it's keyed to RGB byte order with an alpha-channel handling that doesn't
// match WPF's premultiplied BGRA layout cleanly. Implementing our own is one
// loop and avoids the conversion ambiguity.
internal sealed class BgraLuminanceSource : LuminanceSource
{
    private readonly byte[] _luminance;

    public BgraLuminanceSource(byte[] bgra, int width, int height, int stride) : base(width, height)
    {
        _luminance = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            var srcRow = y * stride;
            var dstRow = y * width;
            for (int x = 0; x < width; x++)
            {
                var src = srcRow + x * 4;
                var b = bgra[src + 0];
                var g = bgra[src + 1];
                var r = bgra[src + 2];
                // ITU-R BT.601 luma. Same coefficients ZXing uses internally.
                _luminance[dstRow + x] = (byte)((306 * r + 601 * g + 117 * b + 512) >> 10);
            }
        }
    }

    public override byte[] getRow(int y, byte[]? row)
    {
        if (row is null || row.Length < Width) row = new byte[Width];
        Array.Copy(_luminance, y * Width, row, 0, Width);
        return row;
    }

    public override byte[] Matrix => _luminance;
}
