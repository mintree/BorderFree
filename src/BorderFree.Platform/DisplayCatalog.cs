using System.Runtime.InteropServices;
using BorderFree.Core.Geometry;

namespace BorderFree.Platform;

public sealed record DisplayInfo(
    nint Handle,
    string DeviceName,
    bool IsPrimary,
    PixelRect Bounds,
    PixelRect WorkArea,
    int DpiX,
    int DpiY)
{
    public int ScalePercent => DpiX <= 0 ? 100 : (int)Math.Round(DpiX * 100.0 / 96.0);

    public string ShortLabel
    {
        get
        {
            var role = IsPrimary ? "Primary" : "Display";
            return $"{role} {Bounds.Width}×{Bounds.Height}";
        }
    }

    public string Label =>
        $"{(IsPrimary ? "Primary" : "Display")} — {Bounds.Width}×{Bounds.Height} at {Bounds.X}, {Bounds.Y} · {ScalePercent}%";

    public MonitorSnapshot ToSnapshot() => new(DeviceName, IsPrimary, Bounds, WorkArea);
}

public static class DisplayCatalog
{
    public static IReadOnlyList<DisplayInfo> ListMonitors()
    {
        var found = new List<DisplayInfo>();
        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            if (TryRead(monitor, out var info))
                found.Add(info);
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0))
            throw new InvalidOperationException("Windows could not list the displays.");

        GC.KeepAlive(callback);
        found.Sort(static (a, b) =>
        {
            if (a.IsPrimary != b.IsPrimary)
                return a.IsPrimary ? -1 : 1;
            return string.Compare(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
        });
        return found;
    }

    public static string? DeviceNameFromWindow(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            return null;

        nint monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == 0 || !TryRead(monitor, out var info))
            return null;

        return info.DeviceName;
    }

    public static string? DeviceNameFromRect(PixelRect rect)
    {
        var monitors = ListMonitors();
        int index = MonitorGeometry.IndexOfBestMonitor(rect, monitors.Select(m => m.Bounds).ToList());
        return index >= 0 ? monitors[index].DeviceName : null;
    }

    private static bool TryRead(nint monitor, out DisplayInfo info)
    {
        info = null!;
        var data = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        if (!NativeMethods.GetMonitorInfo(monitor, ref data))
            return false;

        var bounds = data.rcMonitor.ToPixelRect();
        var work = data.rcWork.ToPixelRect();
        if (!bounds.HasArea)
            return false;
        if (!work.HasArea)
            work = bounds;

        var (dpiX, dpiY) = ReadDpi(monitor);
        string name = string.IsNullOrWhiteSpace(data.szDevice) ? "DISPLAY" : data.szDevice;
        info = new DisplayInfo(
            monitor,
            name,
            (data.dwFlags & NativeMethods.MonitorInfoPrimary) != 0,
            bounds,
            work,
            dpiX,
            dpiY);
        return true;
    }

    private static (int X, int Y) ReadDpi(nint monitor)
    {
        try
        {
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
                return ((int)dpiX, (int)dpiY);
        }
        catch (DllNotFoundException)
        {
            // shcore.dll is present on Windows 8.1 and later. Fall through to 96.
        }

        return (96, 96);
    }
}
