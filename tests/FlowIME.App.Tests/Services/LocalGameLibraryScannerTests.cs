using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class LocalGameLibraryScannerTests
{
    [Fact]
    public async Task ScanAsync_discovers_Steam_manifest_and_unique_game_executable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sandbox = new TemporaryDirectory();
        var steamRoot = Path.Combine(sandbox.Path, "Steam");
        var steamApps = Path.Combine(steamRoot, "steamapps");
        var install = Path.Combine(steamApps, "common", "Example Game");
        Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(
            Path.Combine(steamApps, "appmanifest_123.acf"),
            "\"AppState\" { \"appid\" \"123\" \"name\" \"Example Game\" \"installdir\" \"Example Game\" }",
            cancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(install, "ExampleGame.exe"),
            [0x4D, 0x5A],
            cancellationToken);

        var scanner = new LocalGameLibraryScanner([steamRoot], []);

        var result = await scanner.ScanAsync(cancellationToken);

        var game = Assert.Single(result);
        Assert.Equal("Steam", game.SourceName);
        Assert.Equal("123", game.StoreId);
        Assert.Equal("Example Game", game.DisplayName);
        Assert.Equal(Path.Combine(install, "ExampleGame.exe"), game.ExecutablePath);
    }

    [Fact]
    public async Task ScanAsync_keeps_game_unlinked_when_multiple_executables_are_ambiguous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sandbox = new TemporaryDirectory();
        var steamRoot = Path.Combine(sandbox.Path, "Steam");
        var steamApps = Path.Combine(steamRoot, "steamapps");
        var install = Path.Combine(steamApps, "common", "Example Game");
        Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(
            Path.Combine(steamApps, "appmanifest_123.acf"),
            "\"AppState\" { \"appid\" \"123\" \"name\" \"Example Game\" \"installdir\" \"Example Game\" }",
            cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(install, "Client.exe"), [0x4D, 0x5A], cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(install, "Game.exe"), [0x4D, 0x5A], cancellationToken);

        var scanner = new LocalGameLibraryScanner([steamRoot], []);

        var game = Assert.Single(await scanner.ScanAsync(cancellationToken));

        Assert.Null(game.ExecutablePath);
        Assert.Equal(2, game.ExecutableCandidates.Count);
    }

    [Fact]
    public async Task ScanAsync_discovers_Epic_manifest_without_executing_any_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sandbox = new TemporaryDirectory();
        var manifests = Path.Combine(sandbox.Path, "Epic", "Manifests");
        var install = Path.Combine(sandbox.Path, "Games", "Epic Example");
        Directory.CreateDirectory(manifests);
        Directory.CreateDirectory(install);
        await File.WriteAllBytesAsync(Path.Combine(install, "EpicExample.exe"), [0x4D, 0x5A], cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(manifests, "example.item"),
            $$"""{"AppName":"epic-example","DisplayName":"Epic Example","InstallLocation":"{{install.Replace("\\", "\\\\")}}","LaunchExecutable":"EpicExample.exe"}""",
            cancellationToken);

        var scanner = new LocalGameLibraryScanner([], [manifests]);

        var game = Assert.Single(await scanner.ScanAsync(cancellationToken));

        Assert.Equal("Epic", game.SourceName);
        Assert.Equal("epic-example", game.StoreId);
        Assert.Equal(Path.Combine(install, "EpicExample.exe"), game.ExecutablePath);
    }

    [Fact]
    public async Task ScanAsync_ignores_Steam_install_directory_that_escapes_common_root()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sandbox = new TemporaryDirectory();
        var steamRoot = Path.Combine(sandbox.Path, "Steam");
        var steamApps = Path.Combine(steamRoot, "steamapps");
        var outside = Path.Combine(steamRoot, "Outside");
        Directory.CreateDirectory(steamApps);
        Directory.CreateDirectory(outside);
        await File.WriteAllBytesAsync(Path.Combine(outside, "Outside.exe"), [0x4D, 0x5A], cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(steamApps, "appmanifest_999.acf"),
            "\"AppState\" { \"appid\" \"999\" \"name\" \"Outside\" \"installdir\" \"..\\..\\Outside\" }",
            cancellationToken);

        var scanner = new LocalGameLibraryScanner([steamRoot], []);

        Assert.Empty(await scanner.ScanAsync(cancellationToken));
    }

    [Fact]
    public async Task ScanAsync_ignores_malformed_configured_roots()
    {
        var scanner = new LocalGameLibraryScanner(["\0"], ["\0"]);

        var result = await scanner.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"FlowIME-game-library-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
