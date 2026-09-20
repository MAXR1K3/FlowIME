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

    [Fact]
    public void Shutdown_argument_requests_primary_instance_exit()
    {
        Assert.True(LaunchOptions.ShouldRequestExit(["--shutdown"]));
        Assert.True(LaunchOptions.ShouldRequestExit(["--SHUTDOWN"]));
        Assert.False(LaunchOptions.ShouldRequestExit(["--background"]));
    }
}
