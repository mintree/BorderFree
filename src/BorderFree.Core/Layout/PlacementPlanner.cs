using BorderFree.Core.Geometry;
using BorderFree.Core.Settings;

namespace BorderFree.Core.Layout;

public static class PlacementPlanner
{
    public static PixelRect ForPreset(LayoutPreset preset, MonitorSnapshot monitor, bool useWorkArea)
    {
        var area = MonitorSelection.ReferenceArea(monitor, useWorkArea);
        return PresetCalculator.Compute(preset, area);
    }

    public static PixelRect ForProfile(ProgramProfile profile, MonitorSnapshot monitor, bool useWorkArea)
    {
        if (profile.Preset == LayoutPreset.Custom)
        {
            if (profile.Custom is { HasArea: true } custom)
                return custom;

            throw new InvalidOperationException("The custom layout is missing a rectangle.");
        }

        return ForPreset(profile.Preset, monitor, useWorkArea);
    }
}

public static class PresetCycle
{
    public static LayoutPreset Next(LayoutPreset current) => Step(current, 1);

    public static LayoutPreset Previous(LayoutPreset current) => Step(current, -1);

    private static LayoutPreset Step(LayoutPreset current, int delta)
    {
        var cycle = LayoutPresetInfo.Cycle;
        int index = -1;
        for (int i = 0; i < cycle.Count; i++)
        {
            if (cycle[i] == current)
            {
                index = i;
                break;
            }
        }

        // Custom (and anything unknown) sits just outside the cycle:
        // next enters at 16:9, previous enters at the right half.
        if (index < 0)
            return delta > 0 ? cycle[0] : cycle[cycle.Count - 1];

        int next = (index + delta) % cycle.Count;
        if (next < 0)
            next += cycle.Count;
        return cycle[next];
    }
}

public static class AutoApplyPolicy
{
    public const int MinimumWidth = 320;
    public const int MinimumHeight = 240;

    /// <summary>
    /// Splash screens and dialogs of a remembered game are left alone.
    /// </summary>
    public static bool IsCandidateWindow(PixelRect bounds) =>
        bounds.Width >= MinimumWidth && bounds.Height >= MinimumHeight;
}

public static class Safeguard
{
    /// <summary>
    /// True when the window is known to be on a different display than the one we placed it on.
    /// An unknown current display does not count: we only move a window when we can see where it is.
    /// </summary>
    public static bool IsOnWrongMonitor(string? currentDeviceName, string desiredDeviceName)
    {
        if (string.IsNullOrWhiteSpace(currentDeviceName) || string.IsNullOrWhiteSpace(desiredDeviceName))
            return false;

        return !string.Equals(currentDeviceName, desiredDeviceName, StringComparison.OrdinalIgnoreCase);
    }
}
