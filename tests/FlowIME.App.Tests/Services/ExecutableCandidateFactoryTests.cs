using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class ExecutableCandidateFactoryTests
{
    [Fact]
    public void Creates_manual_candidate_for_existing_executable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowIME.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "MyTool.exe");
        File.WriteAllBytes(path, []);

        try
        {
            var candidate = ExecutableCandidateFactory.Create(path);

            Assert.Equal("MyTool", candidate.ProcessName);
            Assert.Equal(Path.GetFullPath(path), candidate.ExecutablePath);
            Assert.Equal((nint)0, candidate.MainWindowHandle);
            Assert.Equal((uint)0, candidate.ProcessId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing.exe")]
    [InlineData("notes.txt")]
    public void Rejects_missing_or_non_executable_paths(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), fileName);

        Assert.Throws<ArgumentException>(() => ExecutableCandidateFactory.Create(path));
    }
}