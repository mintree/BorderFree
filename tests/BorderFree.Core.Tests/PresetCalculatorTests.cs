using BorderFree.Core.Geometry;
using BorderFree.Core.Layout;
using BorderFree.Core.Settings;

namespace BorderFree.Core.Tests;

public class PresetCalculatorTests
{
    private static readonly PixelRect SuperUltrawide = new(0, 0, 5120, 1440);

    [Fact]
    public void Centered16x9_On5120x1440_IsHalfWidthAndCentered()
    {
        var rect = PresetCalculator.Compute(LayoutPreset.Centered16x9, SuperUltrawide);

        Assert.Equal(new PixelRect(1280, 0, 2560, 1440), rect);
    }

    [Fact]
    public void Centered21x9_On5120x1440_UsesExact21By9()
    {
        var rect = PresetCalculator.Compute(LayoutPreset.Centered21x9, SuperUltrawide);

        Assert.Equal(3360, rect.Width);
        Assert.Equal(1440, rect.Height);
        Assert.Equal(880, rect.X);
        Assert.Equal(0, rect.Y);
    }

    [Fact]
    public void Full32x9_FillsTheReferenceRectangle()
    {
        var rect = PresetCalculator.Compute(LayoutPreset.Full32x9, SuperUltrawide);

        Assert.Equal(SuperUltrawide, rect);
    }

    [Fact]
    public void Halves_TileA32x9Panel_AsTwo16x9Frames()
    {
        var left = PresetCalculator.Compute(LayoutPreset.LeftHalf, SuperUltrawide);
        var right = PresetCalculator.Compute(LayoutPreset.RightHalf, SuperUltrawide);

        Assert.Equal(new PixelRect(0, 0, 2560, 1440), left);
        Assert.Equal(new PixelRect(2560, 0, 2560, 1440), right);
        Assert.Equal(left.Right, right.X);
        Assert.Equal(SuperUltrawide.Width, left.Width + right.Width);
    }

    [Theory]
    [InlineData(3839)]
    [InlineData(1921)]
    [InlineData(3440)]
    public void Halves_CoverTheWidthExactly_WhenTheWidthIsOddOrEven(int width)
    {
        var area = new PixelRect(10, 20, width, 1000);
        var left = PresetCalculator.Half(area, left: true);
        var right = PresetCalculator.Half(area, left: false);

        Assert.Equal(area.X, left.X);
        Assert.Equal(left.Right, right.X);
        Assert.Equal(area.Right, right.Right);
        Assert.Equal(area.Y, left.Y);
        Assert.Equal(area.Height, left.Height);
        Assert.Equal(area.Height, right.Height);
        Assert.True(right.Width == left.Width || right.Width == left.Width + 1);
    }

    [Fact]
    public void Presets_HonorAMonitorThatIsNotAtTheOrigin()
    {
        var monitor = new PixelRect(1920, 100, 3840, 1080);

        var centered = PresetCalculator.Compute(LayoutPreset.Centered16x9, monitor);
        var full = PresetCalculator.Compute(LayoutPreset.Full32x9, monitor);

        Assert.Equal(new PixelRect(2880, 100, 1920, 1080), centered);
        Assert.Equal(monitor, full);
    }

    [Fact]
    public void Presets_HonorAMonitorLeftOfThePrimary()
    {
        var monitor = new PixelRect(-2560, -120, 2560, 1440);

        var rect = PresetCalculator.Compute(LayoutPreset.Centered16x9, monitor);

        Assert.Equal(monitor, rect);
    }

    [Fact]
    public void Centered21x9_OnA16x9Panel_LetterboxesVertically()
    {
        var panel = new PixelRect(0, 0, 2560, 1440);

        var rect = PresetCalculator.Compute(LayoutPreset.Centered21x9, panel);

        Assert.Equal(2560, rect.Width);
        Assert.Equal(1097, rect.Height);
        Assert.Equal(0, rect.X);
        Assert.Equal(171, rect.Y);
        Assert.True(rect.Bottom <= panel.Bottom);
    }

    [Fact]
    public void Centered16x9_OnAPortraitArea_FitsTheWidth()
    {
        var portrait = new PixelRect(0, 0, 1080, 1920);

        var rect = PresetCalculator.FitAspect(portrait, 16, 9);

        Assert.Equal(1080, rect.Width);
        Assert.Equal(608, rect.Height);
        Assert.Equal(0, rect.X);
        Assert.Equal(656, rect.Y);
    }

    [Fact]
    public void WorkArea_ChangesTheFrame_WhenTheTaskbarTakesSpace()
    {
        var monitor = new MonitorSnapshot(
            @"\\.\DISPLAY1",
            true,
            new PixelRect(0, 0, 5120, 1440),
            new PixelRect(0, 0, 5120, 1400));

        var fromBounds = PlacementPlanner.ForPreset(LayoutPreset.Centered16x9, monitor, useWorkArea: false);
        var fromWork = PlacementPlanner.ForPreset(LayoutPreset.Centered16x9, monitor, useWorkArea: true);

        Assert.Equal(new PixelRect(1280, 0, 2560, 1440), fromBounds);
        Assert.Equal(1400, fromWork.Height);
        Assert.True(fromWork.Width < fromBounds.Width);
        Assert.Equal(0, fromWork.Y);
        Assert.True(fromWork.X > 0);
    }

    [Fact]
    public void CustomProfile_UsesTheStoredRectangle_AndIgnoresTheWorkArea()
    {
        var monitor = new MonitorSnapshot(@"\\.\DISPLAY1", true, SuperUltrawide, new PixelRect(0, 40, 5120, 1400));
        var profile = new ProgramProfile
        {
            Preset = LayoutPreset.Custom,
            Custom = new PixelRect(-1920, 10, 1920, 1080)
        };

        var rect = PlacementPlanner.ForProfile(profile, monitor, useWorkArea: true);

        Assert.Equal(new PixelRect(-1920, 10, 1920, 1080), rect);
    }

    [Fact]
    public void CustomProfile_WithoutARectangle_Throws()
    {
        var monitor = new MonitorSnapshot("DISPLAY1", true, SuperUltrawide, SuperUltrawide);
        var profile = new ProgramProfile { Preset = LayoutPreset.Custom };

        Assert.Throws<InvalidOperationException>(() => PlacementPlanner.ForProfile(profile, monitor, false));
    }

    [Fact]
    public void EmptyArea_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PresetCalculator.Compute(LayoutPreset.Centered16x9, new PixelRect(0, 0, 0, 100)));
    }

    [Fact]
    public void WidthOfOne_CannotBeSplit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PresetCalculator.Half(new PixelRect(0, 0, 1, 100), left: true));
    }

    [Fact]
    public void CustomPreset_IsNotAComputedShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PresetCalculator.Compute(LayoutPreset.Custom, SuperUltrawide));
    }

    [Fact]
    public void RoundDiv_RoundsHalvesAwayFromZero()
    {
        Assert.Equal(1, PresetCalculator.RoundDiv(1, 1, 2));
        Assert.Equal(2560, PresetCalculator.RoundDiv(1440, 16, 9));
        Assert.Equal(3360, PresetCalculator.RoundDiv(1440, 21, 9));
    }

    [Fact]
    public void FullPreset_OnANon32x9Monitor_StillFillsThatMonitor()
    {
        var panel = new PixelRect(100, 50, 1920, 1080);

        Assert.Equal(panel, PresetCalculator.Compute(LayoutPreset.Full32x9, panel));
    }
}
