using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Windows.Context;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Tests.Context;

public sealed class GameplayEvidenceProbeTests
{
    [Fact]
    public void Direct3d_exclusive_notification_state_emits_strong_evidence()
    {
        var probe = new Direct3DFullscreenEvidenceProbe(
            new FixedNotificationProbe(QueryUserNotificationState.RunningDirect3DFullScreen));

        var evidence = probe.Capture(Window());

        Assert.NotNull(evidence);
        Assert.Equal(GameplayEvidenceKind.Direct3DExclusive, evidence.Kind);
        Assert.Equal(ContextSignalConfidence.High, evidence.Confidence);
    }

    [Fact]
    public void Ambiguous_or_non_game_shell_states_emit_no_evidence()
    {
        QueryUserNotificationState[] states =
        [
            QueryUserNotificationState.Busy,
            QueryUserNotificationState.PresentationMode,
            QueryUserNotificationState.AcceptsNotifications
        ];

        foreach (var state in states)
        {
            var probe = new Direct3DFullscreenEvidenceProbe(new FixedNotificationProbe(state));
            Assert.Null(probe.Capture(Window()));
        }
    }

    [Fact]
    public void Windows_game_metadata_emits_evidence_and_is_cached()
    {
        var reader = new CountingGameConfigReader(true);
        var probe = new GameConfigStoreEvidenceProbe(reader);

        var first = probe.Capture(Window());
        var second = probe.Capture(Window());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(GameplayEvidenceKind.WindowsGameMetadata, first.Kind);
        Assert.Equal(1, reader.ReadCount);
    }

    [Fact]
    public void Missing_executable_path_skips_game_config_store()
    {
        var reader = new CountingGameConfigReader(true);
        var probe = new GameConfigStoreEvidenceProbe(reader);

        var evidence = probe.Capture(Window() with { ExecutablePath = null });

        Assert.Null(evidence);
        Assert.Equal(0, reader.ReadCount);
    }

    [Theory]
    [InlineData(@"C:\Games\Demo\game.exe", @"c:\games\demo\GAME.EXE")]
    [InlineData(@"C:\Games\Demo\game.exe", @"C:/Games/Demo/game.exe")]
    [InlineData("\"C:\\Games\\Demo\\game.exe\"", @"C:\Games\Demo\game.exe")]
    public void Game_config_path_matching_is_case_separator_and_quote_tolerant(
        string left,
        string right)
    {
        Assert.True(WindowsGameConfigStoreReader.PathsEqual(left, right));
    }

    private static WindowContext Window() =>
        new(
            (nint)0x1234,
            10,
            20,
            "game",
            @"C:\Games\Demo\game.exe",
            "Game",
            "GameWindow",
            null);

    private sealed class FixedNotificationProbe(QueryUserNotificationState? state)
        : IUserNotificationStateProbe
    {
        public QueryUserNotificationState? Capture() => state;
    }

    private sealed class CountingGameConfigReader(bool result) : IGameConfigStoreReader
    {
        public int ReadCount { get; private set; }

        public bool IsKnownGame(string executablePath)
        {
            ReadCount++;
            return result;
        }
    }
}
