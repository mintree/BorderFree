namespace BorderFree.Core.Instances;

public enum ExistingInstanceAction
{
    /// <summary>Ask the running copy to show its window, then exit this one.</summary>
    SignalAndExit,

    /// <summary>Close the running copy and take over as the only instance.</summary>
    Replace
}

/// <summary>
/// Decides what a new launch does when another BorderFree process already holds the single-instance mutex.
/// An older copy (for example v1.1.0 still hidden after X) must not swallow the launch of a newer download.
/// </summary>
public static class StaleInstancePolicy
{
    /// <param name="currentPath">This process's EXE path.</param>
    /// <param name="currentVersion">This EXE's file version, or null when unknown.</param>
    /// <param name="otherPath">The running copy's EXE path, or null when it can't be read (for example it runs elevated).</param>
    /// <param name="otherVersion">The running copy's file version, or null when unknown.</param>
    public static ExistingInstanceAction Decide(
        string? currentPath,
        Version? currentVersion,
        string? otherPath,
        Version? otherVersion)
    {
        // Can't see the other process's file: leave it alone and hand off, as before.
        if (string.IsNullOrWhiteSpace(otherPath) || string.IsNullOrWhiteSpace(currentPath))
            return ExistingInstanceAction.SignalAndExit;

        bool versionsKnown = IsKnown(currentVersion) && IsKnown(otherVersion);

        // Never close a newer copy from an older download.
        if (versionsKnown && otherVersion! > currentVersion!)
            return ExistingInstanceAction.SignalAndExit;

        if (versionsKnown && otherVersion! < currentVersion!)
            return ExistingInstanceAction.Replace;

        if (!SamePath(currentPath, otherPath))
            return ExistingInstanceAction.Replace;

        return ExistingInstanceAction.SignalAndExit;
    }

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // FileVersionInfo can return "1.1.2.0" or "1, 1, 2, 0" or carry trailing text.
        var cleaned = text.Trim().Replace(", ", ".").Replace(',', '.');
        int end = 0;
        while (end < cleaned.Length && (char.IsDigit(cleaned[end]) || cleaned[end] == '.'))
            end++;
        cleaned = cleaned[..end].Trim('.');
        return Version.TryParse(cleaned, out var version) && IsKnown(version) ? version : null;
    }

    public static bool SamePath(string a, string b)
    {
        static string Normalize(string path) =>
            path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');

        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnown(Version? version) =>
        version != null && version != new Version(0, 0) && version != new Version(0, 0, 0, 0);
}
