using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Context;

public sealed class InputContextEngineTests
{
    [Fact]
    public async Task Always_emits_application_signal_and_preserves_focus_hint()
    {
        var engine = new InputContextEngine();
        var request = Request(focusHwnd: (nint)0x77);

        var context = await engine.ResolveAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(context.HasSignal(InputContextSignalKind.Application));
        Assert.Equal((nint)0x77, context.FocusHwnd);
        Assert.Equal(InputContextTrigger.FocusChanged, context.Trigger);
    }

    [Fact]
    public async Task Detector_failure_is_isolated_and_later_detector_still_runs()
    {
        var engine = new InputContextEngine([
            new ThrowingDetector(),
            new FixedDetector()
        ]);

        var context = await engine.ResolveAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.True(context.HasSignal(InputContextSignalKind.Application));
        Assert.True(context.HasSignal(InputContextSignalKind.TextInput));
    }



    [Fact]
    public async Task Game_text_entry_without_game_is_removed_fail_closed()
    {
        var engine = new InputContextEngine([new GameTextEntryOnlyDetector()]);

        var context = await engine.ResolveAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.False(context.HasSignal(InputContextSignalKind.GameTextEntry));
        Assert.False(context.HasSignal(InputContextSignalKind.TextInput));
    }

    [Fact]
    public async Task Game_text_entry_with_game_derives_generic_text_input()
    {
        var engine = new InputContextEngine([
            new GameDetector(),
            new GameTextEntryOnlyDetector()
        ]);

        var context = await engine.ResolveAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.True(context.HasSignal(InputContextSignalKind.Game));
        Assert.True(context.HasSignal(InputContextSignalKind.GameTextEntry));
        Assert.True(context.HasSignal(InputContextSignalKind.TextInput));
    }

    [Fact]
    public void Duplicate_detector_ids_are_rejected_at_registration_time()
    {
        Assert.Throws<ArgumentException>(() =>
            new InputContextEngine([new FixedDetector(), new FixedDetector()]));
    }

    private static ContextDetectionRequest Request(nint focusHwnd = default) =>
        new(
            new WindowContext(
                (nint)0x10,
                1,
                2,
                "Code",
                @"C:\Apps\Code.exe",
                "Code",
                "Chrome_WidgetWin_1",
                null),
            InputContextTrigger.FocusChanged,
            focusHwnd,
            DateTimeOffset.UtcNow);

    private sealed class ThrowingDetector : IInputContextDetector
    {
        public string Id => "00.throw";
        public int Order => 0;

        public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
            ContextDetectionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("expected test failure");
    }

    private sealed class FixedDetector : IInputContextDetector
    {
        public string Id => "10.fixed";
        public int Order => 10;

        public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
            ContextDetectionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
                [new(InputContextSignalKind.TextInput, Id)]);
    }
    private sealed class GameDetector : IInputContextDetector
    {
        public string Id => "20.game";
        public int Order => 20;

        public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
            ContextDetectionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
                [new(InputContextSignalKind.Game, Id)]);
    }

    private sealed class GameTextEntryOnlyDetector : IInputContextDetector
    {
        public string Id => "30.game-text-entry";
        public int Order => 30;

        public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
            ContextDetectionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
                [new(InputContextSignalKind.GameTextEntry, Id)]);
    }

}
