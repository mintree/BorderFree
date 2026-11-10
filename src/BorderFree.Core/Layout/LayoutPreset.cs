namespace BorderFree.Core.Layout;

public enum LayoutPreset
{
    Centered16x9,
    Centered21x9,
    Full32x9,
    LeftHalf,
    RightHalf,
    Custom
}

public static class LayoutPresetInfo
{
    public static readonly IReadOnlyList<LayoutPreset> Cycle =
    [
        LayoutPreset.Centered16x9,
        LayoutPreset.Centered21x9,
        LayoutPreset.Full32x9,
        LayoutPreset.LeftHalf,
        LayoutPreset.RightHalf
    ];

    public static string DisplayName(LayoutPreset preset) => preset switch
    {
        LayoutPreset.Centered16x9 => "16:9 centered",
        LayoutPreset.Centered21x9 => "21:9 centered",
        LayoutPreset.Full32x9 => "Full 32:9",
        LayoutPreset.LeftHalf => "Left half",
        LayoutPreset.RightHalf => "Right half",
        LayoutPreset.Custom => "Custom",
        _ => preset.ToString()
    };

    public static string Description(LayoutPreset preset) => preset switch
    {
        LayoutPreset.Centered16x9 => "Largest 16:9 that fits, centered on the display",
        LayoutPreset.Centered21x9 => "Largest exact 21:9 that fits, centered on the display",
        LayoutPreset.Full32x9 => "The entire selected display",
        LayoutPreset.LeftHalf => "Left half of the display. This is 16:9 when the display is 32:9",
        LayoutPreset.RightHalf => "Right half of the display. This is 16:9 when the display is 32:9",
        LayoutPreset.Custom => "Exact width, height, and position",
        _ => string.Empty
    };
}
