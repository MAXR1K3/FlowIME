using FlowIME.App.Services;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.Services;

public sealed class ForegroundContextServiceOrderingTests
{
    [Fact]
    public async Task Delayed_rule_refresh_cannot_overwrite_a_newer_foreground_window()
    {
        var source = new ForegroundSource();
        var first = Window((nint)0x10, "FirstApp");
        var second = Window((nint)0x20, "SecondApp");
        var repository = new CancellationIgnoringRuleRepository();
        var service = new ForegroundContextService(
            source,
            new ImmediateResolver(first, second),
            new CancellationIgnoringContextEngine(),
            new NoTargetDecisionEngine(),
            repository,
            new CancellationIgnoringInputBackend(),
            ignoredProcessId: 999,
            sampleDelay: TimeSpan.Zero);

        service.Start();
        source.Raise(first.Hwnd);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("FirstApp", service.Current?.Window.ProcessName);

        repository.GateNextConfigurationRead();
        var refresh = service.RefreshRuleMatchAsync(TestContext.Current.CancellationToken).AsTask();
        await repository.GatedReadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        source.Raise(second.Hwnd);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("SecondApp", service.Current?.Window.ProcessName);

        repository.ReleaseGatedRead.TrySetResult();
        await refresh;

        Assert.Equal("SecondApp", service.Current?.Window.ProcessName);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Cancelled_older_resolution_cannot_publish_after_the_newer_window()
    {
        var source = new ForegroundSource();
        var oldWindow = Window((nint)0x10, "OldGame");
        var newWindow = Window((nint)0x20, "NewGame");
        var resolver = new OutOfOrderResolver(oldWindow, newWindow);
        var service = new ForegroundContextService(
            source,
            resolver,
            new CancellationIgnoringContextEngine(),
            new NoTargetDecisionEngine(),
            new CancellationIgnoringRuleRepository(),
            new CancellationIgnoringInputBackend(),
            ignoredProcessId: 999,
            sampleDelay: TimeSpan.Zero);
        var published = new List<string>();
        var stalePublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += snapshot =>
        {
            lock (published)
            {
                published.Add(snapshot.Window.ProcessName);
            }
            if (snapshot.Window.Hwnd == oldWindow.Hwnd)
            {
                stalePublished.TrySetResult();
            }
        };

        service.Start();
        source.Raise(oldWindow.Hwnd);
        await resolver.OldRequestStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        source.Raise(newWindow.Hwnd);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("NewGame", service.Current?.Window.ProcessName);

        resolver.ReleaseOldRequest.TrySetResult();
        var completed = await Task.WhenAny(
            stalePublished.Task,
            Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken));

        Assert.NotSame(stalePublished.Task, completed);
        Assert.Equal("NewGame", service.Current?.Window.ProcessName);
        lock (published)
        {
            Assert.DoesNotContain("OldGame", published);
        }
        await service.DisposeAsync();
    }

    private static WindowContext Window(nint hwnd, string name) =>
        new(hwnd, (uint)hwnd, (uint)hwnd + 1, name, $@"C:\Games\{name}.exe", name, "GameWindow", null);

    private sealed class ForegroundSource : IForegroundWindowSource
    {
        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;
        public nint Current { get; private set; }
        public nint GetCurrentForegroundWindow() => Current;
        public void Raise(nint hwnd)
        {
            Current = hwnd;
            ForegroundWindowChanged?.Invoke(
                this,
                new ForegroundWindowChangedEventArgs(hwnd, DateTimeOffset.UtcNow));
        }
        public void Dispose() { }
    }

    private sealed class OutOfOrderResolver(WindowContext oldWindow, WindowContext newWindow) : IWindowResolver
    {
        public TaskCompletionSource OldRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOldRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<WindowContext?> ResolveAsync(
            nint hwnd,
            CancellationToken cancellationToken = default)
        {
            if (hwnd == oldWindow.Hwnd)
            {
                OldRequestStarted.TrySetResult();
                await ReleaseOldRequest.Task.ConfigureAwait(false);
                return oldWindow;
            }

            return hwnd == newWindow.Hwnd ? newWindow : null;
        }
    }

    private sealed class ImmediateResolver(params WindowContext[] windows) : IWindowResolver
    {
        private readonly IReadOnlyDictionary<nint, WindowContext> _windows =
            windows.ToDictionary(window => window.Hwnd);

        public ValueTask<WindowContext?> ResolveAsync(
            nint hwnd,
            CancellationToken cancellationToken = default)
        {
            _windows.TryGetValue(hwnd, out var window);
            return ValueTask.FromResult(window);
        }
    }

    private sealed class CancellationIgnoringInputBackend : IInputMethodBackend
    {
        public ValueTask<InputState> GetStateAsync(
            WindowContext window,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new InputState("test", InputMode.English, (nint)0x04090409));

        public ValueTask<InputOperationResult> ApplyAsync(
            WindowContext window,
            InputAction action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CancellationIgnoringContextEngine : IInputContextEngine
    {
        public ValueTask<InputContextSnapshot> ResolveAsync(
            ContextDetectionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new InputContextSnapshot(
                request.Window,
                FlowIME.Core.Context.ApplicationIdentity.FromWindow(request.Window),
                request.Trigger,
                request.FocusHwnd,
                request.Timestamp,
                []));
    }

    private sealed class NoTargetDecisionEngine : IInputDecisionEngine
    {
        public InputDecision Resolve(
            InputContextSnapshot context,
            RuleConfigurationSnapshot configuration) => InputDecision.None;
    }

    private sealed class CancellationIgnoringRuleRepository : IRuleRepository
    {
        private static readonly RuleConfigurationSnapshot Empty = new([], null);
        private int _gateNextRead;

        public TaskCompletionSource GatedReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseGatedRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void GateNextConfigurationRead() => Interlocked.Exchange(ref _gateNextRead, 1);

        public ValueTask<RuleConfigurationSnapshot> GetConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _gateNextRead, 0) == 0)
            {
                return ValueTask.FromResult(Empty);
            }

            return AwaitGatedReadAsync();
        }

        private async ValueTask<RuleConfigurationSnapshot> AwaitGatedReadAsync()
        {
            GatedReadStarted.TrySetResult();
            await ReleaseGatedRead.Task.ConfigureAwait(false);
            return Empty;
        }

        public ValueTask<IReadOnlyList<ApplicationRule>> GetRulesAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ApplicationRule>>([]);

        public ValueTask ReplaceRulesAsync(
            IReadOnlyList<ApplicationRule> replacement,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<GlobalDefaultTarget?> GetGlobalDefaultAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GlobalDefaultTarget?>(null);

        public ValueTask ReplaceGlobalDefaultAsync(
            GlobalDefaultTarget? target,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
