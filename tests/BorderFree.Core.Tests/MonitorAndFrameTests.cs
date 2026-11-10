using BorderFree.Core.Geometry;
using BorderFree.Core.Layout;

namespace BorderFree.Core.Tests;

public class MonitorAndFrameTests
{
    [Fact]
    public void BestMonitor_IsTheOneWithTheLargestOverlap()
    {
        var monitors = new PixelRect[]
        {
            new(0, 0, 5120, 1440),
            new(5120, 0, 1920, 1080)
        };
        var mostlyOnPrimary = new PixelRect(5000, 100, 200, 400);

        Assert.Equal(0, MonitorGeometry.IndexOfBestMonitor(mostlyOnPrimary, monitors));
    }

    [Fact]
    public void BestMonitor_ReturnsNegative_WhenTheWindowMissesEveryDisplay()
    {
        var monitors = new PixelRect[] { new(0, 0, 1920, 1080) };
        var elsewhere = new PixelRect(-4000, -4000, 100, 100);

        Assert.Equal(-1, MonitorGeometry.IndexOfBestMonitor(elsewhere, monitors));
        Assert.Equal(0, MonitorGeometry.IntersectionArea(elsewhere, monitors[0]));
    }

    [Fact]
    public void Choose_PrefersTheNamedDisplay_ThenPrimary_ThenFirst()
    {
        var monitors = new MonitorSnapshot[]
        {
            new(@"\\.\DISPLAY2", false, new PixelRect(5120, 0, 1920, 1080), new PixelRect(5120, 0, 1920, 1040)),
            new(@"\\.\DISPLAY1", true, new PixelRect(0, 0, 5120, 1440), new PixelRect(0, 0, 5120, 1400))
        };

        Assert.Equal(@"\\.\DISPLAY2", MonitorSelection.Choose(monitors, @"\\.\display2")!.DeviceName);
        Assert.Equal(@"\\.\DISPLAY1", MonitorSelection.Choose(monitors, @"\\.\DISPLAY9")!.DeviceName);
        Assert.Equal(@"\\.\DISPLAY1", MonitorSelection.Choose(monitors, null)!.DeviceName);
        Assert.Null(MonitorSelection.Choose([], "anything"));
    }

    [Fact]
    public void Choose_UsesTheFirstDisplay_WhenNoneIsPrimary()
    {
        var monitors = new MonitorSnapshot[]
        {
            new("A", false, new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 100)),
            new("B", false, new PixelRect(100, 0, 100, 100), new PixelRect(100, 0, 100, 100))
        };

        Assert.Equal("A", MonitorSelection.Choose(monitors, "missing")!.DeviceName);
    }

    [Fact]
    public void FrameFit_ExpandsTheOuterRectangle_ByTheInvisibleMargin()
    {
        var desired = new PixelRect(1280, 0, 2560, 1440);
        var outer = new PixelRect(1272, -8, 2576, 1456);
        var visible = new PixelRect(1280, 0, 2560, 1440);

        var corrected = FrameFit.TryOuterForVisible(desired, outer, visible, out var result);

        Assert.True(corrected);
        Assert.Equal(new PixelRect(1272, -8, 2576, 1456), result);
    }

    [Fact]
    public void FrameFit_LeavesTheRectangleAlone_WhenThereIsNoMargin()
    {
        var desired = new PixelRect(0, 0, 800, 600);

        var corrected = FrameFit.TryOuterForVisible(desired, desired, desired, out var result);

        Assert.False(corrected);
        Assert.Equal(desired, result);
    }

    [Fact]
    public void FrameFit_RejectsANegativeOrHugeMargin()
    {
        var desired = new PixelRect(0, 0, 800, 600);
        var outer = new PixelRect(0, 0, 800, 600);
        var visible = new PixelRect(-10, 0, 820, 600);

        Assert.False(FrameFit.TryOuterForVisible(desired, outer, visible, out var result));
        Assert.Equal(desired, result);
        Assert.False(new FrameExtras(80, 0, 0, 0).IsPlausible);
    }

    [Fact]
    public void Safeguard_OnlyFlagsAKnownDifferentDisplay()
    {
        Assert.True(Safeguard.IsOnWrongMonitor(@"\\.\DISPLAY2", @"\\.\DISPLAY1"));
        Assert.False(Safeguard.IsOnWrongMonitor(@"\\.\display1", @"\\.\DISPLAY1"));
        Assert.False(Safeguard.IsOnWrongMonitor(null, @"\\.\DISPLAY1"));
        Assert.False(Safeguard.IsOnWrongMonitor("", @"\\.\DISPLAY1"));
    }

    [Theory]
    [InlineData(320, 240, true)]
    [InlineData(319, 240, false)]
    [InlineData(1920, 239, false)]
    [InlineData(2560, 1440, true)]
    public void AutoApply_SkipsSmallWindows(int width, int height, bool candidate)
    {
        Assert.Equal(candidate, AutoApplyPolicy.IsCandidateWindow(new PixelRect(0, 0, width, height)));
    }
}
