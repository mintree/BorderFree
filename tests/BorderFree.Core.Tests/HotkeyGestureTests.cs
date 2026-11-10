using BorderFree.Core.Hotkeys;

namespace BorderFree.Core.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F11", "Ctrl+Alt+F11")]
    [InlineData("ctrl + alt + f11", "Ctrl+Alt+F11")]
    [InlineData("Control+ALT+f10", "Ctrl+Alt+F10")]
    [InlineData("Shift+Win+F1", "Shift+Win+F1")]
    [InlineData("Windows+A", "Win+A")]
    public void Parse_NormalizesModifiersAndKey(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.True(gesture.IsValid);
        Assert.Equal(expected, gesture.ToSettingString());
    }

    [Theory]
    [InlineData("F11")]
    [InlineData("Ctrl")]
    [InlineData("")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+Ctrl+F11")]
    [InlineData("Alt+F11+F12")]
    public void Parse_RejectsBareKeys_UnknownKeys_AndDuplicates(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void VirtualKeys_MatchTheWin32Constants()
    {
        Assert.True(HotkeyKeys.TryGetVirtualKey("F11", out int f11));
        Assert.Equal(0x7A, f11);
        Assert.True(HotkeyKeys.TryGetVirtualKey("a", out int a));
        Assert.Equal(0x41, a);
        Assert.True(HotkeyKeys.TryGetVirtualKey("Left", out int left));
        Assert.Equal(0x25, left);
        Assert.False(HotkeyKeys.TryGetVirtualKey("NotAKey", out _));
    }
}
