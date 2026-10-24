using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Context;

public sealed class GameTextEntryProfileRegistryTests
{
    [Fact]
    public void Empty_registry_has_no_profile()
    {
        var registry = new GameTextEntryProfileRegistry();

        Assert.Null(registry.Resolve(Application()));
    }

    [Fact]
    public void Resolves_enabled_profile_by_stable_application_identity()
    {
        var application = Application();
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "chat",
                ApplicationIdentityKey = application.Key,
                DetectionMode = GameTextEntryDetectionMode.HotkeyProfile,
                ProviderId = InputMethodProviderIds.WeChat,
                Action = InputAction.Chinese,
                EnterGestures = [new(0x54)],
                ExitGestures = [new(0x0D)]
            }
        ]);

        var profile = registry.Resolve(application);

        Assert.NotNull(profile);
        Assert.Equal("chat", profile!.Id);
        Assert.Equal(InputAction.Chinese, profile.Action);
        Assert.Single(profile.EnterGestures);
    }

    [Fact]
    public void Chinese_or_english_target_requires_provider()
    {
        var registry = new GameTextEntryProfileRegistry();

        Assert.Throws<ArgumentException>(() => registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "bad",
                ApplicationIdentityKey = Application().Key,
                DetectionMode = GameTextEntryDetectionMode.Manual,
                Action = InputAction.Chinese
            }
        ]));
    }

    [Fact]
    public void Multiple_enabled_profiles_for_same_application_are_rejected()
    {
        var key = Application().Key;
        var registry = new GameTextEntryProfileRegistry();

        Assert.Throws<ArgumentException>(() => registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "a",
                ApplicationIdentityKey = key,
                DetectionMode = GameTextEntryDetectionMode.Manual
            },
            new GameTextEntryProfile
            {
                Id = "b",
                ApplicationIdentityKey = key,
                DetectionMode = GameTextEntryDetectionMode.HotkeyProfile,
                EnterGestures = [new(0x54)],
                ExitGestures = [new(0x0D)]
            }
        ]));
    }


    [Fact]
    public void Hotkey_profile_requires_enter_and_exit_gestures()
    {
        var registry = new GameTextEntryProfileRegistry();

        Assert.Throws<ArgumentException>(() => registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "bad-hotkey",
                ApplicationIdentityKey = Application().Key,
                DetectionMode = GameTextEntryDetectionMode.HotkeyProfile,
                Action = InputAction.Keep
            }
        ]));
    }

    [Fact]
    public void Display_name_is_normalized_without_changing_identity()
    {
        var application = Application();
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "chat",
                ApplicationIdentityKey = application.Key,
                ApplicationDisplayName = " Game\nTitle ",
                DetectionMode = GameTextEntryDetectionMode.StandardTextControl
            }
        ]);

        var profile = Assert.Single(registry.Profiles);
        Assert.Equal("Game Title", profile.ApplicationDisplayName);
    }
    [Fact]
    public void Resolves_by_explicit_additional_path_but_not_same_exe_name()
    {
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([
            new GameTextEntryProfile
            {
                Id = "chat",
                ApplicationIdentityKey = "path:declared",
                ExecutablePath = @"C:\Games\one\game.exe",
                AdditionalExecutablePaths = [@"C:\Games\two\game.exe"]
            }
        ]);

        Assert.NotNull(registry.Resolve(Application("path:declared", @"C:\Games\one\game.exe")));
        Assert.NotNull(registry.Resolve(Application("path:other", @"C:\Games\two\game.exe")));
        Assert.Null(registry.Resolve(Application("path:other", @"C:\Games\three\game.exe")));
    }

    [Fact]
    public void Conflicting_additional_paths_are_rejected_even_when_profiles_are_disabled()
    {
        var registry = new GameTextEntryProfileRegistry();
        Assert.Throws<ArgumentException>(() => registry.ReplaceProfiles([
            new GameTextEntryProfile { Id = "a", ApplicationIdentityKey = "path:a", ExecutablePath = @"C:\Games\same.exe" },
            new GameTextEntryProfile { Id = "b", ApplicationIdentityKey = "path:b", Enabled = false, AdditionalExecutablePaths = [@"C:\Games\same.exe"] }
        ]));
    }

    [Fact]
    public void Weak_identity_does_not_match_without_explicit_path()
    {
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([new GameTextEntryProfile
        {
            Id = "weak",
            ApplicationIdentityKey = "process:game",
            DetectionMode = GameTextEntryDetectionMode.Manual
        }]);

        Assert.Null(registry.Resolve(Application("process:game", @"C:\Games\other.exe")));
    }

    [Theory]
    [InlineData(@"game.exe")]
    [InlineData(@"C:\Games")]
    [InlineData(@"C:\Games\game.dll")]
    [InlineData(@"\\?\C:\Games\game.exe")]
    public void Invalid_executable_paths_are_rejected(string path)
    {
        var registry = new GameTextEntryProfileRegistry();
        Assert.Throws<ArgumentException>(() => registry.ReplaceProfiles([new GameTextEntryProfile
        {
            Id = "invalid",
            ApplicationIdentityKey = "path:test",
            ExecutablePath = path,
            DetectionMode = GameTextEntryDetectionMode.Manual
        }]));
    }

    [Fact]
    public void Executable_paths_are_full_path_normalized_and_defensively_copied()
    {
        var additional = new[] { @"C:\Games\one\..\two\game.exe" };
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([new GameTextEntryProfile
        {
            Id = "normalized",
            ApplicationIdentityKey = "path:test",
            ExecutablePath = @"C:\Games\one\..\main.exe",
            AdditionalExecutablePaths = additional,
            DetectionMode = GameTextEntryDetectionMode.Manual
        }]);

        additional[0] = @"C:\Games\mutated.exe";
        var profile = Assert.Single(registry.Profiles);
        Assert.Equal(@"C:\Games\two\game.exe", profile.AdditionalExecutablePaths[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)profile.AdditionalExecutablePaths).Add(@"C:\Games\x.exe"));
    }

    [Fact]
    public void Invalid_classification_is_rejected()
    {
        var registry = new GameTextEntryProfileRegistry();
        Assert.Throws<ArgumentException>(() => registry.ReplaceProfiles([new GameTextEntryProfile
        {
            Id = "invalid-classification",
            ApplicationIdentityKey = "path:test",
            Classification = (GameplayClassification)99,
            DetectionMode = GameTextEntryDetectionMode.Manual
        }]));
    }

    [Fact]
    public void Same_executable_path_conflicts_with_its_derived_path_identity()
    {
        var registry = new GameTextEntryProfileRegistry();
        var identity = ApplicationIdentity.FromWindow(new WindowContext(
            (nint)0x10,
            10,
            11,
            "game",
            @"C:\Games\same.exe",
            "Game",
            "GameWindow",
            null));
        Assert.Throws<ArgumentException>(() => registry.ReplaceProfiles([
            new GameTextEntryProfile { Id = "identity", ApplicationIdentityKey = identity.Key, DetectionMode = GameTextEntryDetectionMode.Manual },
            new GameTextEntryProfile { Id = "path", ApplicationIdentityKey = "path:other", ExecutablePath = @"C:\Games\same.exe", DetectionMode = GameTextEntryDetectionMode.Manual }
        ]));
    }

    [Fact]
    public void Chat_resolution_filters_not_game_but_declared_resolution_does_not()
    {
        var application = Application();
        var registry = new GameTextEntryProfileRegistry();
        registry.ReplaceProfiles([new GameTextEntryProfile
        {
            Id = "not-game",
            ApplicationIdentityKey = application.Key,
            Classification = GameplayClassification.NotGame
        }]);

        Assert.Null(registry.Resolve(application));
        Assert.Equal("not-game", registry.ResolveDeclaredProfile(application)!.Id);
    }

    private static ApplicationIdentity Application(string key, string? path)
    {
        var window = new WindowContext((nint)0x10, 10, 11, "game", path, "Game", "GameWindow", null);
        return ApplicationIdentity.FromWindow(window) with { Key = key };
    }

    private static ApplicationIdentity Application()
    {
        var window = new WindowContext(
            (nint)0x10,
            10,
            11,
            "game",
            @"C:\Games\game.exe",
            "Game",
            "GameWindow",
            null);
        return ApplicationIdentity.FromWindow(window);
    }
}
