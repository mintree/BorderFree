namespace BorderFree.Core.Startup;

/// <summary>
/// The current user's "start this program when Windows starts" entry.
/// The Windows implementation writes HKCU\...\Run. Tests use a fake.
/// </summary>
public interface IStartupStore
{
    /// <summary>The stored command, or null when BorderFree is not registered.</summary>
    string? Get();

    void Set(string command);

    void Remove();
}
