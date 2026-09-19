using FlowIME.App.Services;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Tests.Services;

public sealed class ForegroundContextServiceTests
{
    [Fact]
    public async Task Captures_external_foreground_state_and_ignores_the_FlowIME_window()
    {
        var source = new FakeForegroundWindowSource();
        var resolver = new FakeWindowResolver(
            Window((nint)0x10, 700, "Code", @"C:\Apps\Code.exe"),
            Window((nint)0x20, 999, "FlowIME.App", @"C:\FlowIME\FlowIME.App.exe"));
        var repository = new FakeRuleRepository([
            new ApplicationRule(
                Guid.NewGuid(),
                true,
                100,
                new ApplicationMatch(ProcessPath: @"C:\Apps\Code.exe"),
                InputAction.English)
        ]);
        var service = new ForegroundContextService(
            source,
            resolver,
            new RuleEngine(),
            repository,
            new FakeInputBackend(InputMode.English),
            ignoredProcessId: 999,
            sampleDelay: TimeSpan.Zero);

        service.Start();
        source.Raise((nint)0x10);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);

        var current = Assert.IsType<CurrentStateSnapshot>(service.Current);
        Assert.Equal("Code", current.Window.ProcessName);
        Assert.Equal(InputMode.English, current.Input.Mode);
        Assert.Equal(InputAction.English, current.MatchedAction);
        Assert.NotNull(current.Context);
        Assert.Equal(FlowIME.Core.Context.InputContextTrigger.ForegroundChanged, current.Context.Trigger);
        Assert.Equal(FlowIME.Core.Decisions.InputDecisionSource.ApplicationRule, current.Decision?.Source);
        var matchedRule = Assert.IsType<ApplicationRule>(current.MatchedRule);
        Assert.Equal(InputAction.English, matchedRule.Action);

        source.Raise((nint)0x20);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);

        current = Assert.IsType<CurrentStateSnapshot>(service.Current);
        Assert.Equal("Code", current.Window.ProcessName);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Rule_match_refresh_updates_status_without_resampling_input_state()
    {
        var source = new FakeForegroundWindowSource();
        var window = Window((nint)0x10, 700, "Code", @"C:\Apps\Code.exe");
        var resolver = new FakeWindowResolver(window);
        var repository = new FakeRuleRepository([
            new ApplicationRule(
                Guid.NewGuid(),
                true,
                100,
                new ApplicationMatch(ProcessPath: window.ExecutablePath),
                InputAction.English)
        ]);
        var backend = new FakeInputBackend(InputMode.English);
        var service = new ForegroundContextService(
            source,
            resolver,
            new RuleEngine(),
            repository,
            backend,
            ignoredProcessId: 999,
            sampleDelay: TimeSpan.Zero);

        service.Start();
        source.Raise(window.Hwnd);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.GetStateCount);
        Assert.Equal(InputAction.English, service.Current?.MatchedAction);

        await repository.ReplaceRulesAsync([], TestContext.Current.CancellationToken);
        await service.RefreshRuleMatchAsync(TestContext.Current.CancellationToken);

        Assert.Null(service.Current?.MatchedAction);
        Assert.Null(service.Current?.MatchedRule);
        Assert.Equal(1, backend.GetStateCount);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Unmatched_application_projects_global_default_into_current_status()
    {
        var source = new FakeForegroundWindowSource();
        var window = Window((nint)0x10, 700, "Explorer", @"C:\Windows\explorer.exe");
        var resolver = new FakeWindowResolver(window);
        var repository = new FakeRuleRepository(
            [],
            new GlobalDefaultTarget(InputMethodProviderIds.WeChat, InputAction.Chinese));
        var service = new ForegroundContextService(
            source,
            resolver,
            new RuleEngine(),
            repository,
            new FakeInputBackend(InputMode.English),
            ignoredProcessId: 999,
            sampleDelay: TimeSpan.Zero);

        service.Start();
        source.Raise(window.Hwnd);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);

        var current = Assert.IsType<CurrentStateSnapshot>(service.Current);
        Assert.Equal(InputAction.Chinese, current.MatchedAction);
        Assert.Null(current.MatchedRule);
        Assert.Equal(InputMethodProviderIds.WeChat, current.MatchedProviderId);
        Assert.Equal(RuleResolutionSource.GlobalDefault, current.ResolutionSource);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Focus_change_refreshes_current_status_without_mutating_input()
    {
        var source = new FakeForegroundWindowSource();
        var window = Window((nint)0x10, 700, "Code", @"C:\Apps\Code.exe");
        var resolver = new FakeWindowResolver(window);
        var repository = new FakeRuleRepository([
            new ApplicationRule(
                Guid.NewGuid(),
                true,
                100,
                new ApplicationMatch(ProcessPath: window.ExecutablePath),
                InputAction.English)
        ]);
        var backend = new FakeInputBackend(InputMode.English);
        var service = new ForegroundContextService(
            source,
            resolver,
            new RuleEngine(),
            repository,
            backend,
            ignoredProcessId: 999,
            sampleDelay: TimeSpan.Zero);

        service.Start();
        source.Raise(window.Hwnd);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);

        backend.Mode = InputMode.Chinese;
        source.RaiseFocus((nint)0x7777);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.Chinese, service.Current?.Input.Mode);
        Assert.Equal((nint)0x7777, service.Current?.Context?.FocusHwnd);
        Assert.Equal(FlowIME.Core.Context.InputContextTrigger.FocusChanged, service.Current?.Context?.Trigger);
        Assert.Equal(2, backend.GetStateCount);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Explicit_current_refresh_resamples_state_after_system_recovery()
    {
        var source = new FakeForegroundWindowSource();
        var window = Window((nint)0x10, 700, "Code", @"C:\Apps\Code.exe");
        var resolver = new FakeWindowResolver(window);
        var repository = new FakeRuleRepository([]);
        var backend = new FakeInputBackend(InputMode.English);
        var service = new ForegroundContextService(
            source,
            resolver,
            new RuleEngine(),
            repository,
            backend,
            ignoredProcessId: 999,
            sampleDelay: TimeSpan.Zero);

        service.Start();
        source.Raise(window.Hwnd);
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InputMode.English, service.Current?.Input.Mode);

        backend.Mode = InputMode.Chinese;
        service.RequestCurrentRefresh();
        await service.WaitForIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InputMode.Chinese, service.Current?.Input.Mode);
        Assert.Equal(2, backend.GetStateCount);
        await service.DisposeAsync();
    }

    private static WindowContext Window(
        nint hwnd,
        uint processId,
        string processName,
        string path) =>
        new(hwnd, processId, processId + 1, processName, path, processName, "Window", null);

    private sealed class FakeForegroundWindowSource : IForegroundWindowSource, IInputFocusSource
    {
        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;
        public event EventHandler<InputFocusChangedEventArgs>? InputFocusChanged;
        public nint Current { get; private set; }
        public nint GetCurrentForegroundWindow() => Current;
        public void Raise(nint hwnd)
        {
            Current = hwnd;
            ForegroundWindowChanged?.Invoke(
                this,
                new ForegroundWindowChangedEventArgs(hwnd, DateTimeOffset.UtcNow));
        }
        public void RaiseFocus(nint hwnd) =>
            InputFocusChanged?.Invoke(
                this,
                new InputFocusChangedEventArgs(hwnd, 0, 0, DateTimeOffset.UtcNow));
        public void Dispose() { }
    }

    private sealed class FakeWindowResolver(params WindowContext[] windows) : IWindowResolver
    {
        private readonly IReadOnlyDictionary<nint, WindowContext> _windows =
            windows.ToDictionary(window => window.Hwnd);
        public ValueTask<WindowContext?> ResolveAsync(nint hwnd, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _windows.TryGetValue(hwnd, out var window);
            return ValueTask.FromResult(window);
        }
    }

    private sealed class FakeInputBackend(InputMode mode) : IInputMethodBackend
    {
        public InputMode Mode { get; set; } = mode;
        public int GetStateCount { get; private set; }

        public ValueTask<InputState> GetStateAsync(WindowContext window, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetStateCount++;
            return ValueTask.FromResult(
                new InputState("Microsoft Pinyin", Mode, (nint)0x08040804));
        }

        public ValueTask<InputOperationResult> ApplyAsync(WindowContext window, InputAction action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRuleRepository(
        IReadOnlyList<ApplicationRule> rules,
        GlobalDefaultTarget? globalDefault = null) : IRuleRepository
    {
        private IReadOnlyList<ApplicationRule> _rules = rules;
        private GlobalDefaultTarget? _globalDefault = globalDefault;

        public ValueTask<RuleConfigurationSnapshot> GetConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new RuleConfigurationSnapshot(_rules, _globalDefault));
        }

        public ValueTask<IReadOnlyList<ApplicationRule>> GetRulesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_rules);
        }

        public ValueTask ReplaceRulesAsync(
            IReadOnlyList<ApplicationRule> replacement,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _rules = replacement.ToArray();
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
}
