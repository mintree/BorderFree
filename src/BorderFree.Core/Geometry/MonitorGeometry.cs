namespace BorderFree.Core.Geometry;

/// <summary>
/// A display BorderFree can target. Device names match Win32 names such as <c>\\.\DISPLAY1</c>.
/// </summary>
public sealed record MonitorSnapshot(string DeviceName, bool IsPrimary, PixelRect Bounds, PixelRect WorkArea);

public static class MonitorSelection
{
    /// <summary>
    /// Picks the named display, otherwise the primary, otherwise the first one.
    /// A missing or unplugged name falls back instead of failing the layout.
    /// </summary>
    public static MonitorSnapshot? Choose(IReadOnlyList<MonitorSnapshot> monitors, string? deviceName)
    {
        if (monitors.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            foreach (var monitor in monitors)
            {
                if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    return monitor;
            }
        }

        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary)
                return monitor;
        }

        return monitors[0];
    }

    public static PixelRect ReferenceArea(MonitorSnapshot monitor, bool useWorkArea) =>
        useWorkArea ? monitor.WorkArea : monitor.Bounds;
}

public static class MonitorGeometry
{
    public static long IntersectionArea(PixelRect a, PixelRect b)
    {
        int left = Math.Max(a.X, b.X);
        int top = Math.Max(a.Y, b.Y);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0)
            return 0;

        return (long)width * height;
    }

    /// <summary>
    /// Index of the monitor that shares the largest area with the rectangle.
    /// This is the same rule <c>MonitorFromRect</c> uses. Returns -1 when nothing intersects.
    /// </summary>
    public static int IndexOfBestMonitor(PixelRect window, IReadOnlyList<PixelRect> monitorBounds)
    {
        int best = -1;
        long bestArea = 0;
        for (int i = 0; i < monitorBounds.Count; i++)
        {
            long area = IntersectionArea(window, monitorBounds[i]);
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }

        return best;
    }
}
