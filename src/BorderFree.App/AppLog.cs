using System.Globalization;
using System.IO;

namespace BorderFree;

/// <summary>
/// Appends timestamped lines to %AppData%\BorderFree\borderfree.log. Logging never throws.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = ResolvePath();

    public static void Write(string message)
    {
        try
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);

                File.AppendAllText(FilePath, line);
            }
        }
        catch (Exception)
        {
            // A log failure must never take the app down.
        }
    }

    private static string ResolvePath()
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root))
                root = Path.GetTempPath();
            return Path.Combine(root, "BorderFree", "borderfree.log");
        }
        catch (Exception)
        {
            return Path.Combine(Path.GetTempPath(), "borderfree.log");
        }
    }
}
