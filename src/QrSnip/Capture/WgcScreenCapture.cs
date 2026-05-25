using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using QrSnip.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace QrSnip.Capture;

// Real Windows Graphics Capture implementation. Enumerates monitors,
// captures one frame per monitor via WGC, reads the pixels off the
// IDirect3DSurface via Win2D's CanvasBitmap, and packages them as
// CapturedMonitor records.
//
// WGC is a streaming API; we only want one frame. Pattern adapted from
// dotnet/maui's CaptureHelper.Windows.cs: create the frame pool, start the
// session, await the first FrameArrived, dispose everything. Expect a few
// frames of latency on the first capture (GPU warmup).
//
// Per CLAUDE.md §6 Problem 3:
//   - IsCursorCaptureEnabled = false: keep the mouse pointer out of frames.
//   - IsBorderRequired = false: suppress the colored "capturing" indicator,
//     only available on Windows 11 build 20348+. We try/catch the property
//     setter on older OSes.
public sealed class WgcScreenCapture : IScreenCapture
{
    public async Task<CapturedMonitor[]> CaptureAllMonitorsAsync()
    {
        Diagnostics.LogVerbose("WgcScreenCapture: entering CaptureAllMonitorsAsync");
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new PlatformNotSupportedException(
                "Windows Graphics Capture is not supported on this machine. " +
                "Required: Windows 10 build 17134 (April 2018) or later, " +
                "and a GPU/display driver that supports WGC.");
        }
        Diagnostics.LogVerbose("WgcScreenCapture: GraphicsCaptureSession.IsSupported() = true");

        var monitors = MonitorEnumerator.EnumerateAll();
        Diagnostics.LogVerbose($"WgcScreenCapture: enumerated {monitors.Count} monitor(s)");
        using var canvasDevice = new CanvasDevice();
        Diagnostics.LogVerbose("WgcScreenCapture: CanvasDevice constructed");

        var results = new CapturedMonitor[monitors.Count];
        for (int i = 0; i < monitors.Count; i++)
        {
            Diagnostics.LogVerbose($"WgcScreenCapture: capturing monitor [{i}] hMon=0x{monitors[i].Handle:X}");
            results[i] = await CaptureOneMonitorAsync(monitors[i], canvasDevice);
            Diagnostics.LogVerbose($"WgcScreenCapture: monitor [{i}] done, {results[i].Width}x{results[i].Height}");
        }
        return results;
    }

    private static async Task<CapturedMonitor> CaptureOneMonitorAsync(MonitorInfo monitor, CanvasDevice device)
    {
        var item = GraphicsCaptureItemInterop.CreateForMonitor(monitor.Handle);
        var bitmap = await CaptureSingleFrameAsync(item, device);

        // Win2D CanvasBitmap → BGRA byte[]. GetPixelBytes returns tightly-packed
        // pixels in B8G8R8A8UIntNormalized order matching our IQrDecoder contract.
        var pixels = bitmap.GetPixelBytes();
        var width = (int)bitmap.SizeInPixels.Width;
        var height = (int)bitmap.SizeInPixels.Height;
        var stride = width * 4;

        bitmap.Dispose();

        return new CapturedMonitor(
            DesktopX: monitor.X,
            DesktopY: monitor.Y,
            Width: width,
            Height: height,
            Stride: stride,
            Pixels: pixels,
            DpiScale: monitor.DpiScale);
    }

    private static Task<CanvasBitmap> CaptureSingleFrameAsync(GraphicsCaptureItem item, CanvasDevice device)
    {
        Diagnostics.LogVerbose($"CaptureSingleFrameAsync: item.Size={item.Size.Width}x{item.Size.Height}");
        var tcs = new TaskCompletionSource<CanvasBitmap>();

        var framePool = Direct3D11CaptureFramePool.Create(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 1,
            item.Size);
        Diagnostics.LogVerbose("framePool created");

        var session = framePool.CreateCaptureSession(item);
        Diagnostics.LogVerbose("session created");
        session.IsCursorCaptureEnabled = false;

        // IsBorderRequired only exists on Windows 11 build 20348+, and even
        // then only when targeting a newer Windows SDK than our floor of
        // 10.0.19041. Set via reflection so we stay compatible across SDK
        // versions; on machines without the property the border appears in
        // captured frames. Acceptable degradation.
        TrySuppressCaptureBorder(session);

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Diagnostics.LogVerbose("OnFrameArrived fired");
            using var frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                Diagnostics.Log("WGC: TryGetNextFrame returned null on first frame");
                tcs.TrySetException(new InvalidOperationException("WGC returned a null frame."));
                return;
            }
            try
            {
                var canvasBitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);
                Diagnostics.LogVerbose($"OnFrameArrived: bitmap created {canvasBitmap.SizeInPixels.Width}x{canvasBitmap.SizeInPixels.Height}");
                tcs.TrySetResult(canvasBitmap);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("OnFrameArrived", ex);
                tcs.TrySetException(ex);
            }
        }

        framePool.FrameArrived += OnFrameArrived;
        Diagnostics.LogVerbose("FrameArrived handler attached, calling StartCapture()");
        session.StartCapture();
        Diagnostics.LogVerbose("session.StartCapture() returned; awaiting first frame");

        return AwaitFirstFrameThenCleanup(tcs.Task, framePool, session, OnFrameArrived);
    }

    private static void TrySuppressCaptureBorder(GraphicsCaptureSession session)
    {
        try
        {
            var prop = typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired");
            prop?.SetValue(session, false);
        }
        catch
        {
            // Property exists in the type info but the runtime OS doesn't
            // implement it. Accept the colored capture border.
        }
    }

    private static async Task<CanvasBitmap> AwaitFirstFrameThenCleanup(
        Task<CanvasBitmap> frameTask,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session,
        Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object> handler)
    {
        try
        {
            var bitmap = await frameTask;
            // Yield to let the FrameArrived callback fully unwind before we
            // dispose the pool/session it ran on. Per MAUI sample.
            await Task.Yield();
            return bitmap;
        }
        finally
        {
            framePool.FrameArrived -= handler;
            session.Dispose();
            framePool.Dispose();
        }
    }
}
