using FlowIME.Core.Automation;
using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Automation;

public sealed class ManualOverrideGuardTests
{
    [Fact]
    public void Conflicting_automatic_target_is_suppressed_for_same_foreground_session()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
        var guard = new ManualOverrideGuard(timeProvider: clock);
        var context = Context((nint)0x10);
        guard.Register(context, InputMode.Chinese, "test-user");

        var evaluation = guard.Evaluate(
            context,
            Decision(InputAction.English));

        Assert.True(evaluation.Suppress);
        Assert.Equal("manual-override-active", evaluation.Reason);
    }

    [Fact]
    public void Matching_automatic_target_is_not_suppressed()
    {
        var guard = new ManualOverrideGuard();
        var context = Context((nint)0x10);
        guard.Register(context, InputMode.English, "test-user");

        var evaluation = guard.Evaluate(
            context,
            Decision(InputAction.English));

        Assert.False(evaluation.Suppress);
    }

    [Fact]
    public void Leaving_foreground_window_clears_override()
    {
        var guard = new ManualOverrideGuard();
        var context = Context((nint)0x10);
        guard.Register(context, InputMode.Chinese, "test-user");

        guard.NotifyForegroundChanged((nint)0x20);

        Assert.Null(guard.Current);
        Assert.False(guard.Evaluate(context, Decision(InputAction.English)).Suppress);
    }


    [Fact]
    public void Same_window_and_process_keep_override_even_if_identity_resolution_degrades()
    {
        var guard = new ManualOverrideGuard();
        var rich = Context((nint)0x10, packageFamilyName: "Vendor.App_abc");
        guard.Register(rich, InputMode.Chinese, "test-user");
        var degraded = Context((nint)0x10);

        var evaluation = guard.Evaluate(
            degraded,
            Decision(InputAction.English));

        Assert.True(evaluation.Suppress);
    }

    [Fact]
    public void Override_expires_after_bounded_duration()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
        var guard = new ManualOverrideGuard(
            new ManualOverrideOptions { DefaultDuration = TimeSpan.FromSeconds(20) },
            clock);
        var context = Context((nint)0x10);
        guard.Register(context, InputMode.Chinese, "test-user");

        clock.Advance(TimeSpan.FromSeconds(21));

        Assert.Null(guard.Current);
        Assert.False(guard.Evaluate(context, Decision(InputAction.English)).Suppress);
    }

    private static InputContextSnapshot Context(
        nint hwnd,
        string? packageFamilyName = null)
    {
        var window = new WindowContext(
            hwnd,
            10,
            11,
            "Code",
            @"C:\Apps\Code.exe",
            "Code",
            "Window",
            packageFamilyName);
        return new InputContextSnapshot(
            window,
            ApplicationIdentity.FromWindow(window),
            InputContextTrigger.FocusChanged,
            hwnd,
            DateTimeOffset.UtcNow,
            [new(InputContextSignalKind.Application, "test")]);
    }

    private static InputDecision Decision(InputAction action) =>
        new(
            InputDecisionSource.ApplicationRule,
            InputDecisionReasonCode.ApplicationRule,
            "test",
            InputMethodProviderIds.MicrosoftPinyin,
            action);

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }
}
