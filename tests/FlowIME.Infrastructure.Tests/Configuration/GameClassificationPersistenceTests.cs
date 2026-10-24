using System.Text.Json;
using System.Text.Json.Serialization;
using FlowIME.Infrastructure.Configuration;

namespace FlowIME.Infrastructure.Tests.Configuration;

public sealed class GameClassificationPersistenceTests
{
    [Fact]
    public async Task Legacy_profile_becomes_explicit_game_without_rewriting_original_file()
    {
        using var fixture = new Fixture();
        var original = Document("");
        await File.WriteAllTextAsync(fixture.Paths.GameTextEntryProfilesFilePath, original, TestContext.Current.CancellationToken);
        using var repository = new JsonGameTextEntryProfileRepository(fixture.Paths);
        var profile = Assert.Single(await repository.GetProfilesAsync(TestContext.Current.CancellationToken));
        var value = JsonSerializer.SerializeToElement(profile, Options);
        Assert.Equal("Game", value.GetProperty("Classification").GetString());
        Assert.Equal(original, await File.ReadAllTextAsync(fixture.Paths.GameTextEntryProfilesFilePath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Game")]
    [InlineData("NotGame")]
    [InlineData("Automatic")]
    public async Task Classification_and_aliases_survive_save_and_new_repository(string classification)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Paths.GameTextEntryProfilesFilePath,
            Document($"\"Classification\":\"{classification}\",\"AdditionalExecutablePaths\":[\"C:\\\\Games\\\\Test-DX12.exe\"],"),
            TestContext.Current.CancellationToken);
        using (var repository = new JsonGameTextEntryProfileRepository(fixture.Paths))
            await repository.ReplaceProfilesAsync(await repository.GetProfilesAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        using var reloaded = new JsonGameTextEntryProfileRepository(fixture.Paths);
        var profile = Assert.Single(await reloaded.GetProfilesAsync(TestContext.Current.CancellationToken));
        var value = JsonSerializer.SerializeToElement(profile, Options);
        Assert.Equal(classification, value.GetProperty("Classification").GetString());
        Assert.Equal(@"C:\Games\Test-DX12.exe", Assert.Single(value.GetProperty("AdditionalExecutablePaths").EnumerateArray()).GetString());
        Assert.False(profile.Enabled);
    }

    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };
    private static string Document(string extra) =>
        "{\"SchemaVersion\":1,\"Profiles\":[{" + extra +
        "\"Id\":\"test\",\"ApplicationIdentityKey\":\"path:test\",\"ExecutablePath\":\"C:\\\\Games\\\\Test.exe\",\"Enabled\":false,\"DetectionMode\":\"StandardTextControl\",\"Action\":\"Keep\"}]}";

    private sealed class Fixture : IDisposable
    {
        internal AppPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "FlowIME.Tests", Guid.NewGuid().ToString("N")));
        internal Fixture() => Directory.CreateDirectory(Paths.RootDirectory);
        public void Dispose() => Directory.Delete(Paths.RootDirectory, true);
    }
}
