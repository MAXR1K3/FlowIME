using FlowIME.Core.Abstractions;
using FlowIME.Core.Automation;
using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.Core.Tests.Automation;

public sealed class AutomationCoordinatorTests
{
    [Fact]
    public async Task Matched_rule_delegates_to_backend_even_when_reported_mode_already_matches()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.English)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, backend.GetStateCount);
        Assert.Equal(1, backend.ApplyCount);
    }


    [Fact]
    public async Task Successful_apply_notifies_decision_observer()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);
        AutomationDecisionRecord? observed = null;
        coordinator.DecisionRecorded += record => observed = record;

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(observed);
        Assert.Equal(AutomationDecisionOutcome.Applied, observed.Outcome);
        Assert.Equal(CodeWindow.Hwnd, observed.Hwnd);
        Assert.Equal(CodeWindow.ThreadId, observed.TargetThreadId);
        Assert.Equal("Fake", observed.Backend);
        Assert.Equal(InputMode.Chinese, observed.BeforeState?.Mode);
        Assert.Equal(InputMode.English, observed.AfterState?.Mode);
        Assert.Equal((nint)0x08040804, observed.BeforeState?.KeyboardLayout);
        Assert.Equal((nint)0x08040804, observed.AfterState?.KeyboardLayout);
        Assert.Equal(TimeSpan.Zero, observed.Duration);
    }

    [Fact]
    public async Task Restarted_application_with_new_hwnd_reapplies_matching_rule()
    {
        var source = new FakeForegroundWindowSource();
        var restarted = CodeWindow with
        {
            Hwnd = (nint)0x2001,
            ProcessId = 110,
            ThreadId = 111
        };
        var resolver = ResolverWith(CodeWindow, ChromeWindow, restarted);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.ApplyCount);

        source.Raise(ChromeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        source.Raise(restarted.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, backend.ApplyCount);
        Assert.Equal(
            [CodeWindow.Hwnd, ChromeWindow.Hwnd, restarted.Hwnd],
            resolver.ResolvedWindows);
    }

    [Fact]
    public async Task Manual_override_is_not_reverted_until_application_is_entered_again()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow, ChromeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal(InputMode.English, backend.CurrentState.Mode);

        backend.CurrentState = State(InputMode.Chinese);
        Assert.Equal(1, backend.ApplyCount);

        source.Raise(ChromeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.ApplyCount);

        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, backend.ApplyCount);
        Assert.Equal(InputMode.English, backend.CurrentState.Mode);
    }

    [Fact]
    public async Task Rapid_focus_changes_only_apply_the_latest_window_rule()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow, ChromeWindow, WeChatWindow);
        var repository = RepositoryWith(
            RuleFor(CodeWindow, InputAction.English),
            RuleFor(ChromeWindow, InputAction.English),
            RuleFor(WeChatWindow, InputAction.Chinese));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.English)
        };
        var delay = new SequencedDelay(blockedCalls: 2);
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            delay);

        coordinator.Start();

        source.Raise(CodeWindow.Hwnd);
        await delay.WaitForCallCountAsync(1, TestContext.Current.CancellationToken);

        source.Raise(ChromeWindow.Hwnd);
        await delay.WaitForCallCountAsync(2, TestContext.Current.CancellationToken);

        source.Raise(WeChatWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([WeChatWindow.Hwnd], resolver.ResolvedWindows);
        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal(InputAction.Chinese, backend.AppliedActions.Single());
    }

    [Fact]
    public async Task Failed_apply_is_retried_once_when_window_is_still_foreground()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(true);
        var delay = new ImmediateDelay();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            delay);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, backend.ApplyCount);
        Assert.Equal(InputMode.English, backend.CurrentState.Mode);
        Assert.Equal(2, delay.CallCount);
    }

    [Theory]
    [InlineData("input-context-unavailable")]
    [InlineData("input-context-inaccessible")]
    public async Task Structurally_unusable_input_context_is_not_retried(string errorCode)
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese),
            FailureErrorCode = errorCode
        };
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(true);
        var delay = new ImmediateDelay();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            delay);
        var observed = new List<AutomationDecisionRecord>();
        coordinator.DecisionRecorded += observed.Add;

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal(1, delay.CallCount);
        var record = Assert.Single(observed);
        Assert.Equal(AutomationDecisionOutcome.ApplyFailed, record.Outcome);
        Assert.Equal(errorCode, record.ErrorCode);
        Assert.Equal("Fake", record.Backend);
        Assert.NotNull(record.BeforeState);
        Assert.NotNull(record.AfterState);
        Assert.Equal(TimeSpan.Zero, record.Duration);
    }

    [Fact]
    public async Task No_matching_rule_does_not_read_or_mutate_input_state()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(ChromeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(ChromeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, backend.GetStateCount);
        Assert.Equal(0, backend.ApplyCount);
    }

    [Fact]
    public async Task Keep_rule_does_not_read_or_mutate_input_state()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = new FakeRuleRepository(
            [RuleFor(CodeWindow, InputAction.Keep)],
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Chinese));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, backend.GetStateCount);
        Assert.Equal(0, backend.ApplyCount);
    }

    [Fact]
    public async Task Stale_window_after_debounce_is_discarded_before_resolution()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        var delay = new ManualReleaseDelay();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            delay);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await delay.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        source.SetCurrent(ChromeWindow.Hwnd);
        delay.Release();
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Empty(resolver.ResolvedWindows);
        Assert.Equal(0, backend.ApplyCount);
    }


    [Fact]
    public async Task Focus_event_after_foreground_entry_reapplies_rule()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.Chinese));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.English)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        var enteredAt = DateTimeOffset.UtcNow;
        coordinator.Start();
        source.Raise(CodeWindow.Hwnd, enteredAt);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.ApplyCount);

        // Simulate the app restoring a different input context after the top-level
        // window became foreground. The first focus event during entry settlement
        // must re-apply the rule against the real text-input control.
        backend.CurrentState = State(InputMode.English);
        source.RaiseFocus(
            CodeWindow.Hwnd,
            enteredAt + TimeSpan.FromMilliseconds(250));
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, backend.ApplyCount);
        Assert.Equal(InputMode.Chinese, backend.CurrentState.Mode);
    }

    [Fact]
    public async Task Focus_change_in_same_foreground_reapplies_rule_even_after_long_delay()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.Chinese));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.English)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        var enteredAt = DateTimeOffset.UtcNow;
        coordinator.Start();
        source.Raise(CodeWindow.Hwnd, enteredAt);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.ApplyCount);

        backend.CurrentState = State(InputMode.English);
        source.RaiseFocus(
            CodeWindow.Hwnd,
            enteredAt + TimeSpan.FromSeconds(2));
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, backend.ApplyCount);
        Assert.Equal(InputMode.Chinese, backend.CurrentState.Mode);
    }

    [Fact]
    public async Task Focus_unavailable_entry_can_retry_on_late_focus_event()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.Chinese));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.English),
            FailureErrorCode = "focus-unavailable"
        };
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(true);
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        var enteredAt = DateTimeOffset.UtcNow;
        coordinator.Start();
        source.Raise(CodeWindow.Hwnd, enteredAt);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, backend.ApplyCount);

        source.RaiseFocus(
            CodeWindow.Hwnd,
            enteredAt + TimeSpan.FromSeconds(3));
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, backend.ApplyCount);
        Assert.Equal(InputMode.Chinese, backend.CurrentState.Mode);
    }

    [Fact]
    public async Task Focus_event_supersedes_pending_foreground_generation()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.Chinese));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.English)
        };
        var delay = new SequencedDelay(blockedCalls: 1);
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            delay);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await delay.WaitForCallCountAsync(1, TestContext.Current.CancellationToken);

        // Child/accessibility HWND is intentionally different. The coordinator
        // must use the current top-level foreground window for rule resolution.
        source.RaiseFocus((nint)0x7777);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([CodeWindow.Hwnd], resolver.ResolvedWindows);
        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal(InputAction.Chinese, backend.AppliedActions.Single());
    }

    [Fact]
    public async Task Rapid_A_to_B_to_A_switch_only_applies_final_A_generation()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow, ChromeWindow);
        var repository = RepositoryWith(
            RuleFor(CodeWindow, InputAction.Chinese),
            RuleFor(ChromeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.English)
        };
        var delay = new SequencedDelay(blockedCalls: 2);
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            delay);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await delay.WaitForCallCountAsync(1, TestContext.Current.CancellationToken);

        source.Raise(ChromeWindow.Hwnd);
        await delay.WaitForCallCountAsync(2, TestContext.Current.CancellationToken);

        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([CodeWindow.Hwnd], resolver.ResolvedWindows);
        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal(InputAction.Chinese, backend.AppliedActions.Single());
    }

    [Fact]
    public async Task Paused_execution_keeps_hooks_live_and_resume_reapplies_current_foreground()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        await coordinator.SetExecutionEnabledAsync(false);
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, backend.ApplyCount);
        Assert.Empty(resolver.ResolvedWindows);

        await coordinator.SetExecutionEnabledAsync(true);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal([CodeWindow.Hwnd], resolver.ResolvedWindows);
        Assert.Equal(InputAction.English, backend.AppliedActions.Single());
    }

    [Fact]
    public async Task Retry_re_resolves_window_context_before_second_apply()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(true);
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([CodeWindow.Hwnd, CodeWindow.Hwnd], resolver.ResolvedWindows);
        Assert.Equal(2, backend.ApplyCount);
    }

    [Fact]
    public async Task Retry_stops_when_foreground_changes_without_waiting_for_an_event()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow, ChromeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(true);
        var delay = new SecondCallGateDelay();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            delay);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await delay.RetryDelayStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Simulate foreground changing before the WinEvent callback arrives. The
        // retry must independently re-read GetForegroundWindow and abort.
        source.SetCurrent(ChromeWindow.Hwnd);
        delay.ReleaseRetry();
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([CodeWindow.Hwnd], resolver.ResolvedWindows);
        Assert.Equal(1, backend.ApplyCount);
    }


    [Fact]
    public async Task Matching_rule_dispatches_its_provider_id_to_the_backend()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var rule = RuleFor(CodeWindow, InputAction.Chinese) with
        {
            ProviderId = InputMethodProviderIds.WeChat
        };
        var repository = RepositoryWith(rule);
        var backend = new FakeInputMethodBackend();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InputMethodProviderIds.WeChat, Assert.Single(backend.AppliedProviderIds));
        Assert.Equal(InputAction.Chinese, Assert.Single(backend.AppliedActions));
    }

    [Fact]
    public async Task Unmatched_application_uses_global_default_provider_and_action()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(ChromeWindow);
        var repository = new FakeRuleRepository(
            Array.Empty<ApplicationRule>(),
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Chinese));
        var backend = new FakeInputMethodBackend();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(ChromeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InputMethodProviderIds.WeChat, Assert.Single(backend.AppliedProviderIds));
        Assert.Equal(InputAction.Chinese, Assert.Single(backend.AppliedActions));
    }

    [Fact]
    public async Task Explicit_application_rule_failure_never_falls_back_to_global_default()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var rule = RuleFor(CodeWindow, InputAction.English);
        var repository = new FakeRuleRepository(
            [rule],
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Chinese));
        var backend = new FakeInputMethodBackend();
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(false);
        backend.ApplyOutcomes.Enqueue(false);
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, backend.ApplyCount);
        Assert.All(
            backend.AppliedProviderIds,
            provider => Assert.Equal(InputMethodProviderIds.MicrosoftPinyin, provider));
        Assert.All(
            backend.AppliedActions,
            action => Assert.Equal(InputAction.English, action));
    }

    [Fact]
    public async Task Ignored_process_is_not_affected_by_global_default()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = new FakeRuleRepository(
            Array.Empty<ApplicationRule>(),
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Chinese));
        var backend = new FakeInputMethodBackend();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            ignoredProcessId: CodeWindow.ProcessId);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, backend.ApplyCount);
    }

    [Fact]
    public async Task Context_policy_can_override_application_rule_without_changing_coordinator_flow()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.Chinese));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        var decisionEngine = new InputDecisionEngine(
            new RuleEngine(),
            [new FixedContextPolicy(InputAction.English)]);
        var journal = new AutomationDecisionJournal();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            decisionEngine: decisionEngine,
            decisionJournal: journal);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([InputAction.English], backend.AppliedActions);
        var record = Assert.Single(journal.GetRecent());
        Assert.Equal(InputDecisionSource.ContextPolicy, record.DecisionSource);
        Assert.Equal("test-policy", record.ContextPolicyId);
    }

    [Fact]
    public async Task Explicit_manual_override_suppresses_focus_reapply_until_foreground_changes()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow, ChromeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend
        {
            CurrentState = State(InputMode.Chinese)
        };
        var journal = new AutomationDecisionJournal();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend,
            decisionJournal: journal);

        coordinator.Start();
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.ApplyCount);

        var context = new InputContextSnapshot(
            CodeWindow,
            ApplicationIdentity.FromWindow(CodeWindow),
            InputContextTrigger.FocusChanged,
            CodeWindow.Hwnd,
            DateTimeOffset.UtcNow,
            [new(InputContextSignalKind.Application, "test")]);
        coordinator.RegisterManualOverride(
            context,
            InputMode.Chinese,
            "test-user",
            TimeSpan.FromMinutes(1));
        backend.CurrentState = State(InputMode.Chinese);

        source.RaiseFocus((nint)0x7777);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal(
            AutomationDecisionOutcome.SuppressedByManualOverride,
            journal.GetRecent(1).Single().Outcome);

        source.Raise(ChromeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);
        source.Raise(CodeWindow.Hwnd);
        await coordinator.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, backend.ApplyCount);
        Assert.Equal(InputMode.English, backend.CurrentState.Mode);
    }

    [Fact]
    public async Task Runtime_snapshot_reports_started_and_execution_gate_state()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = ResolverWith(CodeWindow);
        var repository = RepositoryWith(RuleFor(CodeWindow, InputAction.English));
        var backend = new FakeInputMethodBackend();
        await using var coordinator = CreateCoordinator(
            source,
            resolver,
            repository,
            backend);

        var beforeStart = coordinator.GetRuntimeSnapshot();
        Assert.False(beforeStart.Started);
        Assert.True(beforeStart.ExecutionEnabled);

        coordinator.Start();
        await coordinator.SetExecutionEnabledAsync(false);

        var paused = coordinator.GetRuntimeSnapshot();
        Assert.True(paused.Started);
        Assert.False(paused.ExecutionEnabled);
        Assert.False(paused.OperationInFlight);
        Assert.True(paused.Generation > beforeStart.Generation);
    }

    private static AutomationCoordinator CreateCoordinator(
        FakeForegroundWindowSource source,
        FakeWindowResolver resolver,
        FakeRuleRepository repository,
        FakeInputMethodBackend backend,
        IAutomationDelay? delay = null,
        uint ignoredProcessId = 0,
        IInputContextEngine? contextEngine = null,
        IInputDecisionEngine? decisionEngine = null,
        ManualOverrideGuard? manualOverrideGuard = null,
        AutomationDecisionJournal? decisionJournal = null) =>
        new(
            source,
            resolver,
            new RuleEngine(),
            repository,
            backend,
            new AutomationOptions
            {
                ForegroundDebounce = TimeSpan.FromMilliseconds(75),
                RetryDelay = TimeSpan.FromMilliseconds(35),
                FinalRetryDelay = TimeSpan.FromMilliseconds(100),
                MaxAttempts = 3
            },
            delay ?? new ImmediateDelay(),
            ignoredProcessId: ignoredProcessId,
            contextEngine: contextEngine,
            decisionEngine: decisionEngine,
            manualOverrideGuard: manualOverrideGuard,
            decisionJournal: decisionJournal);

    private static FakeWindowResolver ResolverWith(params WindowContext[] windows) =>
        new(windows.ToDictionary(static window => window.Hwnd));

    private static FakeRuleRepository RepositoryWith(params ApplicationRule[] rules) =>
        new(rules);

    private static ApplicationRule RuleFor(WindowContext window, InputAction action) =>
        new(
            Guid.NewGuid(),
            true,
            100,
            new ApplicationMatch(ProcessPath: window.ExecutablePath),
            action);

    private static InputState State(InputMode mode) =>
        new("Microsoft Pinyin", mode, (nint)0x08040804);

    private static readonly WindowContext CodeWindow = new(
        Hwnd: (nint)0x1001,
        ProcessId: 10,
        ThreadId: 11,
        ProcessName: "Code",
        ExecutablePath: @"C:\Apps\Code.exe",
        WindowTitle: "Program.cs - Visual Studio Code",
        WindowClass: "Chrome_WidgetWin_1",
        PackageFamilyName: null);

    private static readonly WindowContext ChromeWindow = new(
        Hwnd: (nint)0x1002,
        ProcessId: 20,
        ThreadId: 21,
        ProcessName: "chrome",
        ExecutablePath: @"C:\Apps\chrome.exe",
        WindowTitle: "Chrome",
        WindowClass: "Chrome_WidgetWin_1",
        PackageFamilyName: null);

    private static readonly WindowContext WeChatWindow = new(
        Hwnd: (nint)0x1003,
        ProcessId: 30,
        ThreadId: 31,
        ProcessName: "WeChat",
        ExecutablePath: @"C:\Apps\WeChat.exe",
        WindowTitle: "WeChat",
        WindowClass: "WeChatMainWndForPC",
        PackageFamilyName: null);

    private sealed class FakeForegroundWindowSource : IForegroundWindowSource, IInputFocusSource
    {
        private nint _current;

        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

        public event EventHandler<InputFocusChangedEventArgs>? InputFocusChanged;

        public nint GetCurrentForegroundWindow() => _current;

        public void Raise(nint hwnd, DateTimeOffset? timestamp = null)
        {
            _current = hwnd;
            ForegroundWindowChanged?.Invoke(
                this,
                new ForegroundWindowChangedEventArgs(
                    hwnd,
                    timestamp ?? DateTimeOffset.UtcNow));
        }

        public void RaiseFocus(nint hwnd, DateTimeOffset? timestamp = null)
        {
            InputFocusChanged?.Invoke(
                this,
                new InputFocusChangedEventArgs(
                    hwnd,
                    objectId: 0,
                    childId: 0,
                    timestamp ?? DateTimeOffset.UtcNow));
        }

        public void SetCurrent(nint hwnd) => _current = hwnd;

        public void Dispose()
        {
        }
    }

    private sealed class FakeWindowResolver : IWindowResolver
    {
        private readonly IReadOnlyDictionary<nint, WindowContext> _windows;

        public FakeWindowResolver(IReadOnlyDictionary<nint, WindowContext> windows)
        {
            _windows = windows;
        }

        public List<nint> ResolvedWindows { get; } = [];

        public ValueTask<WindowContext?> ResolveAsync(
            nint hwnd,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolvedWindows.Add(hwnd);
            return ValueTask.FromResult(
                _windows.TryGetValue(hwnd, out var window) ? window : null);
        }
    }

    private sealed class FakeRuleRepository : IRuleRepository
    {
        private IReadOnlyList<ApplicationRule> _rules;
        private GlobalDefaultTarget? _globalDefault;

        public FakeRuleRepository(
            IReadOnlyList<ApplicationRule> rules,
            GlobalDefaultTarget? globalDefault = null)
        {
            _rules = rules;
            _globalDefault = globalDefault;
        }

        public ValueTask<RuleConfigurationSnapshot> GetConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new RuleConfigurationSnapshot(_rules, _globalDefault));
        }

        public ValueTask<IReadOnlyList<ApplicationRule>> GetRulesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_rules);
        }

        public ValueTask ReplaceRulesAsync(
            IReadOnlyList<ApplicationRule> rules,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _rules = rules.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask<GlobalDefaultTarget?> GetGlobalDefaultAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_globalDefault);
        }

        public ValueTask ReplaceGlobalDefaultAsync(
            GlobalDefaultTarget? target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _globalDefault = target;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeInputMethodBackend : IInputMethodBackend
    {
        public InputState CurrentState { get; set; } = State(InputMode.Unknown);
        public Queue<bool> ApplyOutcomes { get; } = new();
        public string FailureErrorCode { get; set; } = "fake-failure";
        public List<InputAction> AppliedActions { get; } = [];
        public List<string> AppliedProviderIds { get; } = [];
        public int GetStateCount { get; private set; }
        public int ApplyCount => AppliedActions.Count;

        public ValueTask<InputState> GetStateAsync(
            WindowContext window,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetStateCount++;
            return ValueTask.FromResult(CurrentState);
        }

        public ValueTask<InputOperationResult> ApplyAsync(
            WindowContext window,
            InputAction action,
            CancellationToken cancellationToken = default) =>
            ApplyCoreAsync(
                InputMethodProviderIds.MicrosoftPinyin,
                action,
                cancellationToken);

        public ValueTask<InputOperationResult> ApplyAsync(
            WindowContext window,
            string? providerId,
            InputAction action,
            CancellationToken cancellationToken = default) =>
            ApplyCoreAsync(
                InputMethodProviderIds.Normalize(providerId),
                action,
                cancellationToken);

        private ValueTask<InputOperationResult> ApplyCoreAsync(
            string providerId,
            InputAction action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppliedProviderIds.Add(providerId);
            AppliedActions.Add(action);

            var before = CurrentState;
            var succeeds = ApplyOutcomes.Count == 0 || ApplyOutcomes.Dequeue();
            if (succeeds)
            {
                CurrentState = action switch
                {
                    InputAction.Chinese => State(InputMode.Chinese),
                    InputAction.English => State(InputMode.English),
                    _ => CurrentState
                };
            }

            return ValueTask.FromResult(new InputOperationResult(
                succeeds,
                before,
                CurrentState,
                "Fake",
                succeeds ? null : FailureErrorCode,
                TimeSpan.Zero));
        }
    }

    private sealed class FixedContextPolicy(InputAction action) : IInputContextPolicy
    {
        public string Id => "test-policy";
        public int Priority => 1000;

        public ContextPolicyMatch? Evaluate(InputContextSnapshot context) =>
            new(
                action,
                InputMethodProviderIds.MicrosoftPinyin,
                Specificity: 100,
                Reason: "test-context-policy");
    }

    private sealed class ImmediateDelay : IAutomationDelay
    {
        public int CallCount { get; private set; }

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequencedDelay : IAutomationDelay
    {
        private readonly int _blockedCalls;
        private int _callCount;

        public SequencedDelay(int blockedCalls)
        {
            _blockedCalls = blockedCalls;
        }

        public async ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call <= _blockedCalls)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        public async Task WaitForCallCountAsync(
            int count,
            CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref _callCount) < count)
            {
                await Task.Delay(1, cancellationToken);
            }
        }
    }

    private sealed class SecondCallGateDelay : IAutomationDelay
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public TaskCompletionSource RetryDelayStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call != 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            RetryDelayStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseRetry() => _release.TrySetResult();
    }

    private sealed class ManualReleaseDelay : IAutomationDelay
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }
}
