using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QrSnip.Interop;

// Enumerates physical monitors using EnumDisplayMonitors. Returns one
// MonitorInfo per HMONITOR with its virtual-desktop bounds and DPI.
//
// Bounds are returned in PHYSICAL PIXELS — that's what WGC captures, and
// the freeze-frame design means the overlay paints those pixels directly.
internal static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorInfo> EnumerateAll()
    {
        var results = new List<MonitorInfo>();
        bool Callback(IntPtr hMonitor, IntPtr hdc, ref Rect lprcMonitor, IntPtr data)
        {
            results.Add(BuildMonitorInfo(hMonitor));
            return true;
        }
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"EnumDisplayMonitors failed: Win32 error {Marshal.GetLastWin32Error()}");
        }
        return results;
    }

    private static MonitorInfo BuildMonitorInfo(IntPtr hMonitor)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
        {
            throw new InvalidOperationException(
                $"GetMonitorInfo failed for HMONITOR {hMonitor}: Win32 error {Marshal.GetLastWin32Error()}");
        }

        // Per-monitor DPI. MDT_EFFECTIVE_DPI (0) returns the scale the user
        // actually configured ("125%" -> 120 dpi). 96 dpi is the 100% baseline.
        var hr = GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY);
        var dpiScale = hr == 0 ? dpiX / 96.0 : 1.0;

        return new MonitorInfo(
            Handle: hMonitor,
            X: mi.rcMonitor.Left,
            Y: mi.rcMonitor.Top,
            Width: mi.rcMonitor.Right - mi.rcMonitor.Left,
            Height: mi.rcMonitor.Bottom - mi.rcMonitor.Top,
            DpiScale: dpiScale);
    }

    // --- Win32 declarations ---

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Rect lprcMonitor, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    private enum MonitorDpiType
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
    }
}

internal sealed record MonitorInfo(
    IntPtr Handle,
    int X, int Y,
    int Width, int Height,
    double DpiScale);
