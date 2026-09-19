using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Background_argument_starts_hidden()
    {
        Assert.True(LaunchOptions.ShouldStartHidden(["--background"]));
        Assert.True(LaunchOptions.ShouldStartHidden(["--BACKGROUND"]));
    }

    [Fact]
    public void Normal_launch_starts_visible()
    {
        Assert.False(LaunchOptions.ShouldStartHidden([]));
        Assert.False(LaunchOptions.ShouldStartHidden(["--other"]));
    }
}
