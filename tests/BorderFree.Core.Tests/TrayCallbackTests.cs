using BorderFree.Core.Tray;

namespace BorderFree.Core.Tests;

public class TrayCallbackTests
{
    private static long V4(int message, int iconId = 1) => ((long)iconId << 16) | (uint)message;

    [Theory]
    [InlineData(TrayCallback.WmLButtonUp)]
    [InlineData(TrayCallback.WmLButtonDblClk)]
    [InlineData(TrayCallback.NinSelect)]
    [InlineData(TrayCallback.NinKeySelect)]
    public void LeftClickAndKeyboardSelect_Open_InBothVersions(int message)
    {
        Assert.Equal(TrayAction.Open, TrayCallback.Interpret(message, version4: false));
        Assert.Equal(TrayAction.Open, TrayCallback.Interpret(V4(message), version4: true));
    }

    [Fact]
    public void Version4_ReadsTheLowWordOnly()
    {
        Assert.Equal(TrayCallback.WmContextMenu, TrayCallback.MouseMessage(V4(TrayCallback.WmContextMenu, iconId: 1)));
        Assert.Equal(TrayAction.ShowMenu, TrayCallback.Interpret(V4(TrayCallback.WmContextMenu), version4: true));
    }

    [Fact]
    public void Version4_RightButtonUpDoesNotOpenASecondMenu()
    {
        Assert.Equal(TrayAction.None, TrayCallback.Interpret(V4(TrayCallback.WmRButtonUp), version4: true));
    }

    [Fact]
    public void Version0_RightButtonUpOpensTheMenu()
    {
        Assert.Equal(TrayAction.ShowMenu, TrayCallback.Interpret(TrayCallback.WmRButtonUp, version4: false));
    }

    [Theory]
    [InlineData(0x0200)] // WM_MOUSEMOVE
    [InlineData(0x0201)] // WM_LBUTTONDOWN
    [InlineData(0x0402)] // NIN_BALLOONSHOW
    public void OtherMessages_DoNothing(int message)
    {
        Assert.Equal(TrayAction.None, TrayCallback.Interpret(message, version4: false));
        Assert.Equal(TrayAction.None, TrayCallback.Interpret(V4(message), version4: true));
    }
}
