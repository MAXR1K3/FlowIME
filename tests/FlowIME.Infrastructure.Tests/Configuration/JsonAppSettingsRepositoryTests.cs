using FlowIME.Core.Settings;
using FlowIME.Infrastructure.Configuration;

namespace FlowIME.Infrastructure.Tests.Configuration;

public sealed class JsonAppSettingsRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FlowIME.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Missing_settings_file_returns_safe_defaults()
    {
        using var repository = new JsonAppSettingsRepository(new AppPaths(_root));

        var snapshot = await repository.GetAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.GameplayHotkeyGuard.Enabled);
        Assert.True(snapshot.GameplayHotkeyGuard.BlockWinSpace);
        Assert.True(snapshot.GameplayHotkeyGuard.BlockCtrlSpace);
        Assert.False(snapshot.GameplayHotkeyGuard.BlockLegacyLanguageHotkeys);
        Assert.True(snapshot.GameplayKeyboardBaseline.Enabled);
        Assert.True(snapshot.InputStatusOverlay.Enabled);
    }

    [Fact]
    public async Task Gameplay_hotkey_preferences_round_trip()
    {
        var paths = new AppPaths(_root);
        var expected = new GameplayHotkeyGuardSettings
        {
            Enabled = false,
            BlockWinSpace = false,
            BlockCtrlSpace = true,
            BlockLegacyLanguageHotkeys = true
        };

        using (var repository = new JsonAppSettingsRepository(paths))
        {
            await repository.SaveGameplayHotkeyGuardAsync(
                expected,
                TestContext.Current.CancellationToken);
        }

        using var reloaded = new JsonAppSettingsRepository(paths);
        var snapshot = await reloaded.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, snapshot.GameplayHotkeyGuard);
    }

    [Fact]
    public async Task Gameplay_keyboard_baseline_preferences_round_trip()
    {
        var paths = new AppPaths(_root);
        var expected = new GameplayKeyboardBaselineSettings { Enabled = false };

        using (var repository = new JsonAppSettingsRepository(paths))
        {
            await repository.SaveGameplayKeyboardBaselineAsync(
                expected,
                TestContext.Current.CancellationToken);
        }

        using var reloaded = new JsonAppSettingsRepository(paths);
        var snapshot = await reloaded.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, snapshot.GameplayKeyboardBaseline);
        Assert.Equal(GameplayHotkeyGuardSettings.Default, snapshot.GameplayHotkeyGuard);
    }

    [Fact]
    public async Task Input_status_overlay_preference_round_trips()
    {
        var paths = new AppPaths(_root);
        var expected = new InputStatusOverlaySettings
        {
            Enabled = false,
            Position = InputStatusOverlayPosition.Caret,
            Size = InputStatusOverlaySize.Large,
            OpacityPercent = 78,
            AnimationsEnabled = false
        };

        using (var repository = new JsonAppSettingsRepository(paths))
        {
            await repository.SaveInputStatusOverlayAsync(
                expected,
                TestContext.Current.CancellationToken);
        }

        using var reloaded = new JsonAppSettingsRepository(paths);
        var snapshot = await reloaded.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, snapshot.InputStatusOverlay);
        Assert.Equal(GameplayHotkeyGuardSettings.Default, snapshot.GameplayHotkeyGuard);
        Assert.Equal(GameplayKeyboardBaselineSettings.Default, snapshot.GameplayKeyboardBaseline);
    }

    [Fact]
    public async Task Schema_one_settings_gain_default_gameplay_keyboard_baseline()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppPaths(_root);
        await File.WriteAllTextAsync(
            paths.SettingsFilePath,
            """
            {
              "SchemaVersion": 1,
              "GameplayHotkeyGuard": {
                "Enabled": false,
                "BlockWinSpace": true,
                "BlockCtrlSpace": false,
                "BlockLegacyLanguageHotkeys": false
              }
            }
            """,
            TestContext.Current.CancellationToken);

        using var repository = new JsonAppSettingsRepository(paths);
        var snapshot = await repository.GetAsync(TestContext.Current.CancellationToken);

        Assert.False(snapshot.GameplayHotkeyGuard.Enabled);
        Assert.True(snapshot.GameplayKeyboardBaseline.Enabled);
        Assert.True(snapshot.InputStatusOverlay.Enabled);
    }

    [Fact]
    public async Task Schema_two_settings_gain_default_input_status_overlay()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppPaths(_root);
        await File.WriteAllTextAsync(
            paths.SettingsFilePath,
            """
            {
              "SchemaVersion": 2,
              "GameplayHotkeyGuard": {
                "Enabled": true,
                "BlockWinSpace": true,
                "BlockCtrlSpace": true,
                "BlockLegacyLanguageHotkeys": false
              },
              "GameplayKeyboardBaseline": {
                "Enabled": true
              }
            }
            """,
            TestContext.Current.CancellationToken);

        using var repository = new JsonAppSettingsRepository(paths);
        var snapshot = await repository.GetAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.InputStatusOverlay.Enabled);
    }

    [Fact]
    public async Task Malformed_settings_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppPaths(_root);
        await File.WriteAllTextAsync(
            paths.SettingsFilePath,
            "{ not-json",
            TestContext.Current.CancellationToken);
        using var repository = new JsonAppSettingsRepository(paths);

        var snapshot = await repository.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            GameplayHotkeyGuardSettings.Default,
            snapshot.GameplayHotkeyGuard);
        Assert.Equal(
            GameplayKeyboardBaselineSettings.Default,
            snapshot.GameplayKeyboardBaseline);
        Assert.Equal(
            InputStatusOverlaySettings.Default,
            snapshot.InputStatusOverlay);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }
}
