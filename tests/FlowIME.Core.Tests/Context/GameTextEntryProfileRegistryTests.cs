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
