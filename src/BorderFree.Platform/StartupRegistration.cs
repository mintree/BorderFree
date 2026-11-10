using BorderFree.Core.Startup;
using Microsoft.Win32;

namespace BorderFree.Platform;

/// <summary>
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BorderFree.
/// The current user can write this key; administrator rights are not required.
/// </summary>
public sealed class StartupRegistration : IStartupStore
{
    public const string ValueName = "BorderFree";

    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName);
        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => null
        };
    }

    public void Set(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("A startup command is required.", nameof(command));

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows did not open the Run key.");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
