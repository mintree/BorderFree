using BorderFree.Core.Geometry;
using BorderFree.Core.Layout;

namespace BorderFree.Core.Settings;

public sealed class ProgramProfile
{
    public LayoutPreset Preset { get; set; } = LayoutPreset.Centered16x9;

    public PixelRect? Custom { get; set; }

    /// <summary>
    /// Win32 device name, for example <c>\\.\DISPLAY1</c>. Null follows the current monitor choice.
    /// </summary>
    public string? MonitorDeviceName { get; set; }
}

public sealed class HotkeySettings
{
    public string NextPreset { get; set; } = "Ctrl+Alt+F11";

    public string PreviousPreset { get; set; } = "Ctrl+Alt+F10";

    public string Reapply { get; set; } = "Ctrl+Alt+F12";
}

public sealed class AppSettings
{
    public int Version { get; set; } = SettingsStore.CurrentVersion;

    /// <summary>
    /// When true, the last layout for each .exe is written to disk and offered again next time.
    /// This does not move windows by itself. See <see cref="AutoApplyOnDetect"/>.
    /// </summary>
    public bool RememberPerProgram { get; set; } = true;

    /// <summary>
    /// When true, and remembering is also on, a newly seen window of a remembered .exe
    /// is placed once. Off by default so opening a program never moves it unless you opt in.
    /// </summary>
    public bool AutoApplyOnDetect { get; set; }

    /// <summary>
    /// When true, a window BorderFree already placed is moved back if it shows up on another display.
    /// Off by default so a window you drag to a second monitor stays there.
    /// </summary>
    public bool SafeguardWrongMonitor { get; set; }

    /// <summary>
    /// When false, presets use the full monitor rectangle (the usual borderless frame).
    /// When true, they use the work area so the taskbar stays visible.
    /// </summary>
    public bool UseWorkArea { get; set; }

    /// <summary>
    /// Null means the primary display.
    /// </summary>
    public string? MonitorDeviceName { get; set; }

    public HotkeySettings Hotkeys { get; set; } = new();

    public Dictionary<string, ProgramProfile> Programs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
