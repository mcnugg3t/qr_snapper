using System;
using System.Collections.Generic;

namespace QrSnip.Decoding;

// The decoder seam. Takes raw BGRA pixels + dimensions + stride and returns
// every QR code found in the image.
//
// Why a seam: ZXing.Net is the v1 implementation, but it's plausible we'll
// later add a preprocessing layer (adaptive thresholding, deskew) or swap to
// ZXingCpp.NET if the fixture harness shows low decode rates on real scans.
// This interface defines the contract those changes must preserve.
//
// Why this signature:
//   - ReadOnlySpan<byte>: caller can hand us a slice of a larger buffer
//     without forcing a copy. The decoder may copy internally.
//   - Separate stride parameter: WPF bitmaps don't always have tightly-packed
//     rows. Forcing the caller to pass stride means we never silently misread.
//   - No format enum: this is a QR-only decoder by design. Returning a
//     payload string is all the consumer needs.
public interface IQrDecoder
{
    IReadOnlyList<QrResult> Decode(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int stride);
}

// One decoded QR code. Payload is the decoded text; BoundingBox is the
// pixel-space rectangle of the QR in the input image (useful for the future
// multi-code picker UI, which needs to highlight each detection).
public sealed record QrResult(string Payload, BoundingBox Box);

public readonly record struct BoundingBox(int X, int Y, int Width, int Height);
