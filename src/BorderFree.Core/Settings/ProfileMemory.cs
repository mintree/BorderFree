namespace BorderFree.Core.Settings;

public static class ProfileMemory
{
    public static string NormalizeExeKey(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName))
            throw new ArgumentException("An executable name is required.", nameof(exeName));

        // Split on both separators. Windows paths have to normalize the same way
        // when the tests run on Linux, where Path.GetFileName ignores '\'.
        var trimmed = exeName.Trim();
        int slash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        var file = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        if (string.IsNullOrWhiteSpace(file))
            throw new ArgumentException("An executable name is required.", nameof(exeName));

        return file.ToLowerInvariant();
    }

    public static ProgramProfile? Recall(AppSettings settings, string exeName)
    {
        if (!settings.RememberPerProgram)
            return null;

        var key = NormalizeExeKey(exeName);
        return settings.Programs.TryGetValue(key, out var profile) ? profile : null;
    }

    public static void Remember(AppSettings settings, string exeName, ProgramProfile profile)
    {
        if (!settings.RememberPerProgram)
            return;

        settings.Programs[NormalizeExeKey(exeName)] = profile;
    }
}
