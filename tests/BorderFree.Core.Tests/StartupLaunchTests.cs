using BorderFree.Core.Startup;

namespace BorderFree.Core.Tests;

public class StartupLaunchTests
{
    [Fact]
    public void Enable_WritesTheQuotedExePathAndMinimizedSwitch()
    {
        var store = new FakeStartupStore();

        StartupLaunch.Enable(store, @"C:\Tools\BorderFree.exe");

        Assert.Equal("\"C:\\Tools\\BorderFree.exe\" --minimized", store.Get());
    }

    [Fact]
    public void Disable_RemovesTheEntry()
    {
        var store = new FakeStartupStore();
        StartupLaunch.Enable(store, @"C:\Tools\BorderFree.exe");

        StartupLaunch.Disable(store);

        Assert.Null(store.Get());
        Assert.False(StartupLaunch.ReadToggle(store));
    }

    [Fact]
    public void Reconcile_RewritesTheCommandWhenTheExeMoved()
    {
        var store = new FakeStartupStore();
        store.Set(StartupLaunch.QuoteCommand(@"D:\Old\BorderFree.exe"));

        var result = StartupLaunch.Reconcile(store, startWithWindows: true, @"E:\New\BorderFree.exe");

        Assert.True(result.Registered);
        Assert.True(result.RewroteCommand);
        Assert.Equal("\"E:\\New\\BorderFree.exe\" --minimized", store.Get());
    }

    [Fact]
    public void Reconcile_LeavesAMatchingPathAlone()
    {
        var store = new FakeStartupStore();
        store.Set(StartupLaunch.QuoteCommand(@"C:\Tools\BorderFree.exe"));

        var result = StartupLaunch.Reconcile(store, startWithWindows: true, @"c:\tools\borderfree.exe");

        Assert.True(result.Registered);
        Assert.False(result.RewroteCommand);
        Assert.Equal("\"C:\\Tools\\BorderFree.exe\" --minimized", store.Get());
    }

    [Fact]
    public void Toggle_ReadsTheStoreRatherThanTheSavedSetting()
    {
        var store = new FakeStartupStore();
        var settings = new BorderFree.Core.Settings.AppSettings { StartWithWindows = false };

        Assert.False(StartupLaunch.ReadToggle(store));
        Assert.False(settings.StartWithWindows);

        store.Set(StartupLaunch.QuoteCommand(@"C:\Old\BorderFree.exe"));

        Assert.True(StartupLaunch.ReadToggle(store));
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void MinimizedArgument_IsRecognized()
    {
        Assert.False(LaunchArguments.RequestsMinimized(null));
        Assert.False(LaunchArguments.RequestsMinimized(Array.Empty<string>()));
        Assert.False(LaunchArguments.RequestsMinimized(["--other"]));
        Assert.True(LaunchArguments.RequestsMinimized(["--minimized"]));
        Assert.True(LaunchArguments.RequestsMinimized(["/minimized"]));
        Assert.True(LaunchArguments.RequestsMinimized(["BorderFree.exe", "--Minimized"]));
    }

    [Fact]
    public void StartHidden_FollowsTheMinimizedSwitch_NotTheSetting()
    {
        Assert.False(LaunchArguments.ShouldStartHidden(minimizedSwitchPresent: false, startWithWindows: true));
        Assert.True(LaunchArguments.ShouldStartHidden(minimizedSwitchPresent: true, startWithWindows: false));
        Assert.True(LaunchArguments.ShouldStartHidden(minimizedSwitchPresent: true, startWithWindows: true));
    }

    private sealed class FakeStartupStore : IStartupStore
    {
        private string? _value;

        public string? Get() => _value;

        public void Set(string command) => _value = command;

        public void Remove() => _value = null;
    }
}
