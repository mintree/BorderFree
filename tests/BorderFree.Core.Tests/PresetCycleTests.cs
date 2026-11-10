using BorderFree.Core.Layout;

namespace BorderFree.Core.Tests;

public class PresetCycleTests
{
    [Fact]
    public void Next_WalksTheFiveLayouts_AndWraps()
    {
        var current = LayoutPreset.Centered16x9;
        var seen = new List<LayoutPreset>();
        for (int i = 0; i < LayoutPresetInfo.Cycle.Count; i++)
        {
            seen.Add(current);
            current = PresetCycle.Next(current);
        }

        Assert.Equal(LayoutPresetInfo.Cycle, seen);
        Assert.Equal(LayoutPreset.Centered16x9, current);
    }

    [Fact]
    public void Previous_WrapsToTheRightHalf()
    {
        Assert.Equal(LayoutPreset.RightHalf, PresetCycle.Previous(LayoutPreset.Centered16x9));
        Assert.Equal(LayoutPreset.LeftHalf, PresetCycle.Previous(LayoutPreset.RightHalf));
    }

    [Fact]
    public void Custom_EntersTheCycleFromEitherEnd()
    {
        Assert.Equal(LayoutPreset.Centered16x9, PresetCycle.Next(LayoutPreset.Custom));
        Assert.Equal(LayoutPreset.RightHalf, PresetCycle.Previous(LayoutPreset.Custom));
    }
}
