using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace QrSnip.Interop;

// COM interop bridge for IGraphicsCaptureItemInterop, the API that lets us
// create a GraphicsCaptureItem from an HMONITOR. The C#/WinRT projection
// exposes GraphicsCaptureItem.CreateFromVisual but NOT the monitor/HWND
// overloads, even though they exist in the underlying ABI.
//
// Pattern adapted from dotnet/maui's CaptureHelper.Windows.cs (Microsoft's
// own MAUI device-test infrastructure). The IIDs and interface shape are
// load-bearing — getting either wrong produces silent failures or odd
// "value does not fall within the expected range" exceptions.
internal static class GraphicsCaptureItemInterop
{
    // IID for the GraphicsCaptureItem runtime class itself. Passed to
    // CreateForMonitor so the returned ABI pointer is the right type.
    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForMonitor(IntPtr hMonitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemGuid;
        var itemPointer = interop.CreateForMonitor(hMonitor, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }
}

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}
