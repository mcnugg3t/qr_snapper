using System;
using System.Collections.Generic;
using ZXingCpp;

namespace QrSnip.Decoding;

// IQrDecoder backed by the C++ port of ZXing (NuGet: ZXingCpp 0.5.1+).
//
// Why both this AND ZXingQrDecoder exist:
//   - The .NET port (ZXing.Net) handles clean inputs perfectly and ships as
//     pure managed code.
//   - The C++ port has a substantially better LOCATOR — its finder-pattern
//     detection tolerates more module-edge erosion, which is the failure
//     mode Caleb diagnosed 2026-05-25 on his real lab fixtures.
//   - The C++ port adds a native DLL (ZXing.dll ~1MB per RID), which
//     complicates MSIX packaging slightly but stays within the
//     self-contained constraint.
//
// We keep ZXingQrDecoder around for tests and as a fallback. Production
// uses ZXingCppQrDecoder.
public sealed class ZXingCppQrDecoder : IQrDecoder
{
    public IReadOnlyList<QrResult> Decode(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int stride)
    {
        // ZXingCpp's ImageView takes a raw luminance (single byte per pixel)
        // plane. Build that from BGRA. Using the same BT.601 luma formula as
        // BgraLuminanceSource so preprocessing decisions made against the
        // .NET-port pipeline (the FixtureDecodeTests harness) compare
        // apples-to-apples with what ZXingCpp will see.
        var luma = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            var rowSrc = y * stride;
            var rowDst = y * width;
            for (int x = 0; x < width; x++)
            {
                var src = rowSrc + x * 4;
                luma[rowDst + x] = (byte)((306 * bgra[src + 2] + 601 * bgra[src + 1] + 117 * bgra[src + 0] + 512) >> 10);
            }
        }

        var imageView = new ImageView(luma, width, height, ImageFormat.Lum);
        var reader = new BarcodeReader
        {
            Formats = BarcodeFormat.QRCode,
            TryInvert = true,    // try both polarities (light-on-dark too)
            TryRotate = true,    // already on by default but explicit is clearer
            TryHarder = true,    // do the slower-but-more-thorough scan
        };

        var found = reader.From(imageView);
        var results = new List<QrResult>();
        foreach (var barcode in found)
        {
            var pos = barcode.Position;
            var box = ComputeBoundingBox(pos);
            results.Add(new QrResult(barcode.Text, box));
        }
        return results;
    }

    private static BoundingBox ComputeBoundingBox(Position pos)
    {
        // ZXingCpp's Position has four corner points; take the axis-aligned
        // bounding box of all four. Used by SnipSession (Stage 5) to
        // highlight detections; not load-bearing for decoding.
        var xs = new[] { pos.TopLeft.X, pos.TopRight.X, pos.BottomRight.X, pos.BottomLeft.X };
        var ys = new[] { pos.TopLeft.Y, pos.TopRight.Y, pos.BottomRight.Y, pos.BottomLeft.Y };
        int minX = xs[0], minY = ys[0], maxX = xs[0], maxY = ys[0];
        for (int i = 1; i < 4; i++)
        {
            if (xs[i] < minX) minX = xs[i];
            if (ys[i] < minY) minY = ys[i];
            if (xs[i] > maxX) maxX = xs[i];
            if (ys[i] > maxY) maxY = ys[i];
        }
        return new BoundingBox(minX, minY, maxX - minX, maxY - minY);
    }
}
