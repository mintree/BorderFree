using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BorderFree.Core.Settings;

public readonly record struct LoadResult(AppSettings Settings, bool RecoveredFromCorruptFile, string? Warning);

public sealed class SettingsStore
{
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        IgnoreReadOnlyProperties = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public SettingsStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A settings path is required.", nameof(path));

        _path = path;
    }

    public string FilePath => _path;

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BorderFree");

    public static string DefaultFilePath => Path.Combine(DefaultDirectory, "settings.json");

    public LoadResult Load()
    {
        if (!File.Exists(_path))
            return new LoadResult(new AppSettings(), false, null);

        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LoadResult(new AppSettings(), false, "BorderFree couldn't read its settings file. Defaults are in use for this session.");
        }

        if (string.IsNullOrWhiteSpace(text))
            return new LoadResult(new AppSettings(), false, null);

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(text, JsonOptions) ?? new AppSettings();
            Normalize(settings);
            return new LoadResult(settings, false, null);
        }
        catch (JsonException)
        {
            BackupCorrupt();
            var fresh = new AppSettings();
            try
            {
                Save(fresh);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Defaults still let the app open. The backup of the bad file is already in place.
            }

            return new LoadResult(
                fresh,
                true,
                "Settings were unreadable and have been reset. The previous file was kept beside it as settings.json.bak.");
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void Normalize(AppSettings settings)
    {
        // Version 1 files omit StartWithWindows. Missing bools stay false, then the file
        // is marked current so the next save records the new schema.
        settings.Version = CurrentVersion;

        settings.Hotkeys ??= new HotkeySettings();
        settings.Programs = RebuildPrograms(settings.Programs);
    }

    private static Dictionary<string, ProgramProfile> RebuildPrograms(Dictionary<string, ProgramProfile>? programs)
    {
        var rebuilt = new Dictionary<string, ProgramProfile>(StringComparer.OrdinalIgnoreCase);
        if (programs == null)
            return rebuilt;

        foreach (var pair in programs)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                continue;

            try
            {
                rebuilt[ProfileMemory.NormalizeExeKey(pair.Key)] = pair.Value;
            }
            catch (ArgumentException)
            {
                // Skip a key that is not an executable name.
            }
        }

        return rebuilt;
    }

    private void BackupCorrupt()
    {
        try
        {
            File.Copy(_path, _path + ".bak", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Starting from defaults matters more than keeping an unreadable backup.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // The failed save already surfaces to the caller.
        }
    }
}
