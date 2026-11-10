namespace BorderFree.Core.Startup;

public static class LaunchArguments
{
    public const string MinimizedSwitch = "--minimized";

    public static bool RequestsMinimized(IReadOnlyList<string>? args)
    {
        if (args == null)
            return false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, MinimizedSwitch, StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "/minimized", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Hide the window on startup only when the process was launched with --minimized.
    /// The Start with Windows setting alone does not hide a normal double-click.
    /// The Run key is what passes the switch.
    /// </summary>
    public static bool ShouldStartHidden(bool minimizedSwitchPresent, bool startWithWindows)
    {
        _ = startWithWindows;
        return minimizedSwitchPresent;
    }
}
