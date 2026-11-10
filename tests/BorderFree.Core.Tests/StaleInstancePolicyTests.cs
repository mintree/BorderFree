using BorderFree.Core.Instances;

namespace BorderFree.Core.Tests;

public class StaleInstancePolicyTests
{
    private const string Downloads112 = @"C:\Users\me\Downloads\BorderFree-v1.1.2-win-x64.exe";
    private const string Downloads111 = @"C:\Users\me\Downloads\BorderFree-v1.1.1-win-x64.exe";

    [Fact]
    public void OlderVersionAtAnotherPath_IsReplaced()
    {
        var action = StaleInstancePolicy.Decide(Downloads112, new Version(1, 1, 2, 0), Downloads111, new Version(1, 1, 1, 0));

        Assert.Equal(ExistingInstanceAction.Replace, action);
    }

    [Fact]
    public void OlderVersionAtTheSamePath_IsReplaced()
    {
        var action = StaleInstancePolicy.Decide(Downloads112, new Version(1, 1, 2, 0), Downloads112, new Version(1, 1, 0, 0));

        Assert.Equal(ExistingInstanceAction.Replace, action);
    }

    [Fact]
    public void SamePathAndVersion_SignalsTheRunningCopy()
    {
        var action = StaleInstancePolicy.Decide(Downloads112, new Version(1, 1, 2, 0), Downloads112.ToUpperInvariant(), new Version(1, 1, 2, 0));

        Assert.Equal(ExistingInstanceAction.SignalAndExit, action);
    }

    [Fact]
    public void SameVersionAtAnotherPath_IsReplaced()
    {
        var action = StaleInstancePolicy.Decide(Downloads112, new Version(1, 1, 2, 0), @"D:\Tools\BorderFree.exe", new Version(1, 1, 2, 0));

        Assert.Equal(ExistingInstanceAction.Replace, action);
    }

    [Fact]
    public void NewerRunningCopy_IsNeverClosed()
    {
        var action = StaleInstancePolicy.Decide(Downloads111, new Version(1, 1, 1, 0), Downloads112, new Version(1, 1, 2, 0));

        Assert.Equal(ExistingInstanceAction.SignalAndExit, action);
    }

    [Fact]
    public void UnreadableOtherPath_SignalsTheRunningCopy()
    {
        var action = StaleInstancePolicy.Decide(Downloads112, new Version(1, 1, 2, 0), null, null);

        Assert.Equal(ExistingInstanceAction.SignalAndExit, action);
    }

    [Fact]
    public void UnknownVersions_FallBackToThePath()
    {
        Assert.Equal(ExistingInstanceAction.Replace, StaleInstancePolicy.Decide(Downloads112, null, Downloads111, null));
        Assert.Equal(ExistingInstanceAction.SignalAndExit, StaleInstancePolicy.Decide(Downloads112, null, Downloads112, null));
    }

    [Theory]
    [InlineData("1.1.2.0", "1.1.2.0")]
    [InlineData("1, 1, 2, 0", "1.1.2.0")]
    [InlineData("1.1.1", "1.1.1")]
    [InlineData("1.1.2+abc123", "1.1.2")]
    public void ParseVersion_ReadsFileVersionStrings(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), StaleInstancePolicy.ParseVersion(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.0.0.0")]
    [InlineData("garbage")]
    public void ParseVersion_ReturnsNullForUnknown(string? text)
    {
        Assert.Null(StaleInstancePolicy.ParseVersion(text));
    }

    [Fact]
    public void SamePath_IgnoresCaseQuotesAndSlashes()
    {
        Assert.True(StaleInstancePolicy.SamePath("\"C:\\Tools\\BorderFree.exe\"", "c:/tools/borderfree.EXE"));
        Assert.False(StaleInstancePolicy.SamePath(@"C:\Tools\BorderFree.exe", @"C:\Other\BorderFree.exe"));
    }
}
