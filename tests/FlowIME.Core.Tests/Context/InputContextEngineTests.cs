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

    [Fact]
    public async Task Recent_cache_reuses_an_exact_context_request()
    {
        var detector = new CountingDetector();
        var engine = new RecentInputContextCache(new InputContextEngine([detector]));
        var request = Request();

        var first = await engine.ResolveAsync(request, TestContext.Current.CancellationToken);
        var second = await engine.ResolveAsync(request, TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, detector.CallCount);
    }

    [Fact]
    public async Task Recent_cache_does_not_reuse_a_different_native_event()
    {
        var detector = new CountingDetector();
        var engine = new RecentInputContextCache(new InputContextEngine([detector]));
        var firstRequest = Request();
        var secondRequest = firstRequest with
        {
            Timestamp = firstRequest.Timestamp.AddTicks(1)
        };

        _ = await engine.ResolveAsync(firstRequest, TestContext.Current.CancellationToken);
        _ = await engine.ResolveAsync(secondRequest, TestContext.Current.CancellationToken);

        Assert.Equal(2, detector.CallCount);
    }

    [Fact]
    public async Task Recent_cache_coalesces_concurrent_exact_requests()
    {
        var detector = new BlockingDetector();
        var engine = new RecentInputContextCache(new InputContextEngine([detector]));
        var request = Request();

        var first = engine.ResolveAsync(request, TestContext.Current.CancellationToken).AsTask();
        await detector.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = engine.ResolveAsync(request, TestContext.Current.CancellationToken).AsTask();
        detector.Release.TrySetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, detector.CallCount);
    }

    [Fact]
    public async Task Cancelling_one_waiter_does_not_cancel_the_shared_resolution()
    {
        var detector = new BlockingDetector();
        var engine = new RecentInputContextCache(new InputContextEngine([detector]));
        var request = Request();
        using var cancellation = new CancellationTokenSource();

        var cancelledWaiter = engine.ResolveAsync(request, cancellation.Token).AsTask();
        await detector.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var survivingWaiter = engine.ResolveAsync(request, TestContext.Current.CancellationToken).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
        detector.Release.TrySetResult();

        _ = await survivingWaiter;

        Assert.Equal(1, detector.CallCount);
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

    private sealed class CountingDetector : IInputContextDetector
    {
        public string Id => "40.counting";
        public int Order => 40;
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
            ContextDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>([]);
        }
    }

    private sealed class BlockingDetector : IInputContextDetector
    {
        public string Id => "50.blocking";
        public int Order => 50;
        public int CallCount { get; private set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
            ContextDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return [];
        }
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
