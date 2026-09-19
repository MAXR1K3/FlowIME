using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class StartupRegistrationServiceTests
{
    [Fact]
    public void Startup_command_quotes_executable_and_requests_background_mode()
    {
        var command = StartupRegistrationService.BuildCommandLine(
            @"C:\Program Files\FlowIME\FlowIME.App.exe");

        Assert.Equal(
            "\"C:\\Program Files\\FlowIME\\FlowIME.App.exe\" --background",
            command);
    }

    [Fact]
    public void Startup_command_rejects_invalid_quoted_path()
    {
        Assert.Throws<ArgumentException>(() =>
            StartupRegistrationService.BuildCommandLine("C:\\Bad\"Path\\FlowIME.App.exe"));
    }
    [Fact]
    public void Startup_command_rejects_run_key_command_longer_than_260_characters()
    {
        var path = "C:\\" + new string('a', 250) + "\\FlowIME.App.exe";

        Assert.Throws<InvalidOperationException>(() =>
            StartupRegistrationService.BuildCommandLine(path));
    }

}
