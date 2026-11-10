using BorderFree.Core.Geometry;
using BorderFree.Core.Layout;
using BorderFree.Core.Settings;

namespace BorderFree.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory;

    public SettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "borderfree-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));

        var loaded = store.Load();

        Assert.False(loaded.RecoveredFromCorruptFile);
        Assert.Null(loaded.Warning);
        Assert.True(loaded.Settings.RememberPerProgram);
        Assert.False(loaded.Settings.AutoApplyOnDetect);
        Assert.False(loaded.Settings.SafeguardWrongMonitor);
        Assert.False(loaded.Settings.UseWorkArea);
        Assert.Null(loaded.Settings.MonitorDeviceName);
        Assert.Equal("Ctrl+Alt+F11", loaded.Settings.Hotkeys.NextPreset);
        Assert.Empty(loaded.Settings.Programs);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsProfiles_AndNormalizesTheExeKey()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            RememberPerProgram = true,
            AutoApplyOnDetect = true,
            SafeguardWrongMonitor = true,
            UseWorkArea = true,
            MonitorDeviceName = @"\\.\DISPLAY1",
            Hotkeys = new HotkeySettings
            {
                NextPreset = "Ctrl+Shift+F8",
                PreviousPreset = "Ctrl+Shift+F7",
                Reapply = "Ctrl+Shift+F9"
            }
        };
        ProfileMemory.Remember(settings, @"D:\Games\Live\Game.EXE", new ProgramProfile
        {
            Preset = LayoutPreset.LeftHalf,
            MonitorDeviceName = @"\\.\DISPLAY2",
            Custom = new PixelRect(-1920, 0, 1920, 1080)
        });

        store.Save(settings);
        var loaded = store.Load();
        var profile = ProfileMemory.Recall(loaded.Settings, "game.exe");

        Assert.NotNull(profile);
        Assert.Equal(LayoutPreset.LeftHalf, profile.Preset);
        Assert.Equal(@"\\.\DISPLAY2", profile.MonitorDeviceName);
        Assert.Equal(new PixelRect(-1920, 0, 1920, 1080), profile.Custom);
        Assert.True(loaded.Settings.UseWorkArea);
        Assert.True(loaded.Settings.SafeguardWrongMonitor);
        Assert.Equal("Ctrl+Shift+F8", loaded.Settings.Hotkeys.NextPreset);
        Assert.Contains("\"game.exe\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("rememberPerProgram", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void RememberFlag_Off_DoesNotReadOrWriteProfiles()
    {
        var settings = new AppSettings { RememberPerProgram = false };
        settings.Programs["game.exe"] = new ProgramProfile { Preset = LayoutPreset.RightHalf };

        Assert.Null(ProfileMemory.Recall(settings, "game.exe"));

        ProfileMemory.Remember(settings, "other.exe", new ProgramProfile { Preset = LayoutPreset.Full32x9 });

        Assert.False(settings.Programs.ContainsKey("other.exe"));
        Assert.True(settings.Programs.ContainsKey("game.exe"));
    }

    [Fact]
    public void ExeKeys_AreCaseInsensitive()
    {
        var settings = new AppSettings();
        ProfileMemory.Remember(settings, "ELDENRING.EXE", new ProgramProfile { Preset = LayoutPreset.Centered21x9 });

        var profile = ProfileMemory.Recall(settings, "eldenring.exe");

        Assert.NotNull(profile);
        Assert.Equal(LayoutPreset.Centered21x9, profile.Preset);
    }

    [Fact]
    public void Load_AcceptsAHandWrittenFile()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "rememberPerProgram": true,
              "programs": {
                "Game.EXE": {
                  "preset": "Centered21x9",
                  "monitorDeviceName": "\\\\.\\DISPLAY1"
                }
              }
            }
            """);
        var store = new SettingsStore(path);

        var loaded = store.Load();

        var profile = ProfileMemory.Recall(loaded.Settings, "GAME.exe");
        Assert.NotNull(profile);
        Assert.Equal(LayoutPreset.Centered21x9, profile.Preset);
        Assert.Equal(@"\\.\DISPLAY1", profile.MonitorDeviceName);
    }

    [Fact]
    public void CorruptFile_ResetsSettings_AndKeepsABackup()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new SettingsStore(path);

        var loaded = store.Load();

        Assert.True(loaded.RecoveredFromCorruptFile);
        Assert.NotNull(loaded.Warning);
        Assert.Empty(loaded.Settings.Programs);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Contains("rememberPerProgram", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyExeName_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => ProfileMemory.NormalizeExeKey("   "));
    }

    [Fact]
    public void DefaultPath_LivesUnderBorderFree()
    {
        Assert.EndsWith(Path.Combine("BorderFree", "settings.json"), SettingsStore.DefaultFilePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // The assertions already ran. A locked temp file should not fail the suite.
        }
    }
}
