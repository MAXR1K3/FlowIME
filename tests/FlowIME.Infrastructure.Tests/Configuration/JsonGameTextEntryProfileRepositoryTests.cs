using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Infrastructure.Configuration;

namespace FlowIME.Infrastructure.Tests.Configuration;

public sealed class JsonGameTextEntryProfileRepositoryTests
{
    [Fact]
    public async Task Missing_file_loads_empty_profiles()
    {
        using var fixture = new Fixture();
        using var repository = new JsonGameTextEntryProfileRepository(fixture.Paths);

        var profiles = await repository.GetProfilesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(profiles);
    }

    [Fact]
    public async Task Round_trips_profile_and_gestures()
    {
        using var fixture = new Fixture();
        var expected = Profile();
        using (var repository = new JsonGameTextEntryProfileRepository(fixture.Paths))
        {
            await repository.ReplaceProfilesAsync(
                [expected],
                TestContext.Current.CancellationToken);
        }

        using var reloaded = new JsonGameTextEntryProfileRepository(fixture.Paths);
        var actual = Assert.Single(
            await reloaded.GetProfilesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.ApplicationIdentityKey, actual.ApplicationIdentityKey);
        Assert.Equal(expected.ApplicationDisplayName, actual.ApplicationDisplayName);
        Assert.Equal(expected.ExecutablePath, actual.ExecutablePath);
        Assert.Equal(expected.DetectionMode, actual.DetectionMode);
        Assert.Equal(expected.Action, actual.Action);
        Assert.Equal(expected.ProviderId, actual.ProviderId);
        Assert.Equal(expected.EnterGestures.ToArray(), actual.EnterGestures.ToArray());
        Assert.Equal(expected.ExitGestures.ToArray(), actual.ExitGestures.ToArray());
    }

    [Fact]
    public async Task Malformed_file_fails_safe_to_empty_profiles()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Paths.RootDirectory);
        await File.WriteAllTextAsync(
            fixture.Paths.GameTextEntryProfilesFilePath,
            "{not-json",
            TestContext.Current.CancellationToken);
        using var repository = new JsonGameTextEntryProfileRepository(fixture.Paths);

        var profiles = await repository.GetProfilesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(profiles);
    }


    [Fact]
    public async Task Unknown_schema_fails_safe_to_empty_profiles()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Paths.RootDirectory);
        await File.WriteAllTextAsync(
            fixture.Paths.GameTextEntryProfilesFilePath,
            "{\"SchemaVersion\":99,\"Profiles\":[]}",
            TestContext.Current.CancellationToken);
        using var repository = new JsonGameTextEntryProfileRepository(fixture.Paths);

        var profiles = await repository.GetProfilesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(profiles);
    }

    private static GameTextEntryProfile Profile() =>
        new()
        {
            Id = "game-chat:path:test",
            ApplicationIdentityKey = "path:test",
            ApplicationDisplayName = "Test Game",
            ExecutablePath = @"C:\Games\TestGame.exe",
            DetectionMode = GameTextEntryDetectionMode.StandardTextControl |
                GameTextEntryDetectionMode.HotkeyProfile,
            ProviderId = InputMethodProviderIds.WeChat,
            Action = InputAction.Chinese,
            EnterGestures = [new(0x54), new(0x59)],
            ExitGestures = [new(0x0D), new(0x1B)]
        };

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "FlowIME.Tests",
                Guid.NewGuid().ToString("N"));
            Paths = new AppPaths(Root);
        }

        public string Root { get; }

        public AppPaths Paths { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
