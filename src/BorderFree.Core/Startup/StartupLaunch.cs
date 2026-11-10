namespace BorderFree.Core.Startup;

public readonly record struct StartupSyncResult(bool Registered, bool RewroteCommand);

/// <summary>
/// Decides what the Run key should contain. The store is the actual registry (or a fake in tests).
/// </summary>
public static class StartupLaunch
{
    public static string QuoteCommand(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("The program path is required.", nameof(exePath));
        if (exePath.IndexOf('"') >= 0)
            throw new ArgumentException("The program path cannot contain a quote.", nameof(exePath));

        return $"\"{exePath.Trim()}\" {LaunchArguments.MinimizedSwitch}";
    }

    public static void Enable(IStartupStore store, string exePath) =>
        store.Set(QuoteCommand(exePath));

    public static void Disable(IStartupStore store) =>
        store.Remove();

    /// <summary>
    /// The checkbox follows the store, not the JSON flag. An entry for any path counts as on.
    /// </summary>
    public static bool ReadToggle(IStartupStore store) =>
        !string.IsNullOrWhiteSpace(store.Get());

    /// <summary>
    /// When startup is enabled, make the stored command match this EXE.
    /// A missing entry or a path left behind after the file was moved is rewritten.
    /// When startup is disabled, the store is left for the caller to clear via <see cref="Disable"/>.
    /// </summary>
    public static StartupSyncResult Reconcile(IStartupStore store, bool startWithWindows, string exePath)
    {
        if (!startWithWindows)
            return new StartupSyncResult(ReadToggle(store), false);

        var desired = QuoteCommand(exePath);
        var current = store.Get();
        if (string.Equals(current?.Trim(), desired, StringComparison.OrdinalIgnoreCase))
            return new StartupSyncResult(true, false);

        store.Set(desired);
        return new StartupSyncResult(true, true);
    }
}
