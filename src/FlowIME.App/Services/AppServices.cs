using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Automation;
using FlowIME.Core.Context;
using FlowIME.Core.Decisions;
using FlowIME.Core.InputMethods;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;
using FlowIME.Core.Settings;
using FlowIME.Infrastructure.Configuration;
using FlowIME.Windows.Applications;
using FlowIME.Windows.Context;
using FlowIME.Windows.Input;
using FlowIME.Windows.Windowing;

namespace FlowIME.App.Services;

public sealed class AppServices : IAsyncDisposable
{
    private static readonly TimeSpan SystemRecoveryDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan GameplayExitRestoreDelay = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan GameplayExitRefreshDelay = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan GameTextEntryHotkeyEnterDelay = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan GameTextEntryHotkeyExitDelay = TimeSpan.FromMilliseconds(140);

    private readonly ForegroundWindowSource _foregroundSource;
    private readonly AutomationCoordinator _automation;
    private readonly JsonRuleRepository _ruleRepository;
    private readonly JsonAppSettingsRepository _settingsRepository;
    private readonly JsonGameTextEntryProfileRepository _gameTextEntryProfileRepository;
    private readonly InputMethodProviderRegistry _inputMethodProviders;
    private readonly GameplayEligibilityDetector _gameplayEligibilityDetector;
    private readonly GameTextEntryRuntimeState _gameTextEntryRuntimeState = new();
    private readonly GameTextEntryProfileRegistry _gameTextEntryProfiles = new();
    private readonly GameplayKeyboardBaseline _gameplayKeyboardBaseline;
    private readonly GameplayHotkeyGuard _gameplayHotkeyGuard;
    private readonly GameTextEntryHotkeyMonitor _gameTextEntryHotkeyMonitor;
    private readonly InputStatusOverlay _inputStatusOverlay;
    private readonly KeyboardLayoutInspector _keyboardLayoutInspector = new();
    private readonly IInputProfileInspector _inputProfileInspector = new TsfInputProfileInspector();
    private readonly GameplayExitTransitionTracker _gameplayTransitionTracker = new();
    private readonly AppPaths _paths;
    private readonly RollingFileTraceListener? _automationTraceListener;
    private readonly SemaphoreSlim _automationStateGate = new(1, 1);
    private readonly object _recoverySync = new();
    private readonly object _gameplayExitRestoreSync = new();
    private readonly object _gameTextEntryHotkeySync = new();

    private CancellationTokenSource? _recoveryCancellation;
    private Task _recoveryTask = Task.CompletedTask;
    private CancellationTokenSource? _gameplayExitRestoreCancellation;
    private Task _gameplayExitRestoreTask = Task.CompletedTask;
    private CancellationTokenSource? _gameTextEntryHotkeyCancellation;
    private Task _gameTextEntryHotkeyTask = Task.CompletedTask;
    private RecentGameplayTarget? _recentGameplayTarget;
    private readonly ActiveGameplayTargetTracker _activeGameplayTargetTracker = new();
    private long _gameplayExitRestoreCount;
    private DateTimeOffset? _gameplayExitRestoreLastAt;
    private string _gameplayExitRestoreLastTarget = "none";
    private string _gameplayExitRestoreLastResult = "none";
    private Task _initializationTask = Task.CompletedTask;
    private bool _automationEnabled = true;
    private bool _disposed;

    public AppServices()
    {
        _paths = new AppPaths();
        _automationTraceListener = TryCreateAutomationTraceListener(_paths);

        _ruleRepository = new JsonRuleRepository(_paths);
        _settingsRepository = new JsonAppSettingsRepository(_paths);
        _gameTextEntryProfileRepository = new JsonGameTextEntryProfileRepository(_paths);
        RuleRepository = _ruleRepository;
        RunningApplications = new RunningApplicationCatalog();
        StartupRegistration = new StartupRegistrationService();

        var gameplayKeyboardBaselineState = new GameplayKeyboardBaselineState();
        _gameplayKeyboardBaseline = new GameplayKeyboardBaseline(
            gameplayKeyboardBaselineState,
            GameplayKeyboardBaselineSettings.Default with { Enabled = false });
        _gameplayHotkeyGuard = new GameplayHotkeyGuard(
            GameplayHotkeyGuardSettings.Default with { Enabled = false });
        _gameTextEntryHotkeyMonitor = new GameTextEntryHotkeyMonitor();
        _gameTextEntryHotkeyMonitor.TransitionDetected += OnGameTextEntryHotkeyTransitionDetected;
        _inputStatusOverlay = new InputStatusOverlay(
            InputStatusOverlaySettings.Default with { Enabled = false });
        _foregroundSource = new ForegroundWindowSource();
        _foregroundSource.ForegroundWindowChanged += OnRawForegroundChangedForHotkeyGuard;
        var resolver = new WindowResolver();
        var ruleEngine = new RuleEngine();
        _gameplayEligibilityDetector = new GameplayEligibilityDetector();
        var contextEngine = new RecentInputContextCache(
            new InputContextEngine([
                new FullscreenWindowDetector(),
                _gameplayEligibilityDetector,
                new StandardGameTextEntryDetector(
                    _gameTextEntryProfiles,
                    _gameTextEntryRuntimeState),
                new GameTextEntryRuntimeDetector(_gameTextEntryRuntimeState)
            ]));
        var decisionEngine = new InputDecisionEngine(
            ruleEngine,
            [
                new GameTextEntryPolicy(_gameTextEntryProfiles),
                new GameplayKeyboardBaselinePolicy(gameplayKeyboardBaselineState)
            ]);
        var manualOverrideGuard = new ManualOverrideGuard();
        var decisionJournal = new AutomationDecisionJournal();
        var microsoftPinyin = new MicrosoftPinyinProvider();
        var weChatInputMethod = new WeChatInputMethodProvider();
        _inputMethodProviders = new InputMethodProviderRegistry(
            [microsoftPinyin, weChatInputMethod],
            MicrosoftPinyinProvider.ProviderId);
        var backend = new ProviderInputMethodBackend(_inputMethodProviders);

        _automation = new AutomationCoordinator(
            _foregroundSource,
            resolver,
            ruleEngine,
            RuleRepository,
            backend,
            ignoredProcessId: checked((uint)Environment.ProcessId),
            contextEngine: contextEngine,
            decisionEngine: decisionEngine,
            manualOverrideGuard: manualOverrideGuard,
            decisionJournal: decisionJournal);
        _automation.DecisionRecorded += OnAutomationDecisionRecorded;

        ForegroundContext = new ForegroundContextService(
            _foregroundSource,
            resolver,
            contextEngine,
            decisionEngine,
            RuleRepository,
            backend);
        ForegroundContext.StateChanged += OnForegroundContextStateChanged;
    }

    public IRuleRepository RuleRepository { get; }

    public IRunningApplicationCatalog RunningApplications { get; }

    public ForegroundContextService ForegroundContext { get; }

    public StartupRegistrationService StartupRegistration { get; }

    public IReadOnlyList<FlowIME.Core.Models.InputMethodProviderDescriptor> InputMethodProviders =>
        _inputMethodProviders.Descriptors;

    public string DefaultInputMethodProviderId =>
        _inputMethodProviders.DefaultProvider.Descriptor.Id;

    public string GetInputMethodProviderDisplayName(string? providerId)
    {
        var normalized = FlowIME.Core.Models.InputMethodProviderIds.Normalize(providerId);
        return _inputMethodProviders.TryGetProvider(normalized, out var provider) && provider is not null
            ? provider.Descriptor.DisplayName
            : normalized;
    }

    public bool IsAutomationEnabled => Volatile.Read(ref _automationEnabled);

    public event Action<bool>? AutomationEnabledChanged;

    public event Action<RecentGameplayTarget?>? ActiveGameplayTargetChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TryStartGameplayHotkeyGuard();
        TryStartInputStatusOverlay();
        _automation.Start();
        ForegroundContext.Start();
        _initializationTask = InitializeServicesAsync();
    }

    public async ValueTask SetAutomationEnabledAsync(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _automationStateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsAutomationEnabled == enabled)
            {
                return;
            }

            var previousEnabled = IsAutomationEnabled;
            _gameplayKeyboardBaseline.SetAutomationEnabled(enabled);
            _gameplayHotkeyGuard.SetAutomationEnabled(enabled);
            if (ForegroundContext.Current is { } hotkeyCurrent)
            {
                UpdateGameTextEntryHotkeyMonitorContext(hotkeyCurrent, enabled);
            }
            else
            {
                _gameTextEntryHotkeyMonitor.UpdateContext(
                    enabled,
                    gameContext: false,
                    gameTextEntry: false,
                    profile: null,
                    processName: "none");
            }
            try
            {
                await _automation.SetExecutionEnabledAsync(enabled).ConfigureAwait(false);
            }
            catch
            {
                _gameplayKeyboardBaseline.SetAutomationEnabled(previousEnabled);
                _gameplayHotkeyGuard.SetAutomationEnabled(previousEnabled);
                if (ForegroundContext.Current is { } restoreCurrent)
                {
                    UpdateGameTextEntryHotkeyMonitorContext(restoreCurrent, previousEnabled);
                }
                else
                {
                    _gameTextEntryHotkeyMonitor.UpdateContext(
                        previousEnabled,
                        gameContext: false,
                        gameTextEntry: false,
                        profile: null,
                        processName: "none");
                }
                throw;
            }

            if (!enabled)
            {
                _gameTextEntryRuntimeState.Deactivate("automation-paused");
            }

            Volatile.Write(ref _automationEnabled, enabled);
            if (enabled && ForegroundContext.Current is { } current)
            {
                UpdateGameplayKeyboardBaselineContext(current);
                UpdateGameplayHotkeyGuardContext(current);
                UpdateGameTextEntryHotkeyMonitorContext(current, enabled);
            }

            AutomationEnabledChanged?.Invoke(enabled);
        }
        finally
        {
            _automationStateGate.Release();
        }
    }

    public async ValueTask RefreshAfterRuleChangeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Re-evaluate the actual foreground after either an application rule or
        // global-default change. AutomationCoordinator explicitly ignores the
        // FlowIME process itself, so editing this UI never applies the fallback to
        // FlowIME or mutates the last external application in the background.
        _automation.RequestCurrentReapply(InputContextTrigger.RuleChanged);
        await ForegroundContext
            .RefreshRuleMatchAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public bool ReportManualInputOverride(
        InputMode mode,
        string source = "user",
        TimeSpan? duration = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context = ForegroundContext.Current?.Context;
        if (context is null)
        {
            return false;
        }

        _automation.RegisterManualOverride(context, mode, source, duration);
        return true;
    }

    public void ClearManualInputOverride()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _automation.ClearManualOverride();
    }

    public GameTextEntryRuntimeSnapshot GetGameTextEntryRuntimeSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _gameTextEntryRuntimeState.GetSnapshot();
    }

    public IReadOnlyList<GameTextEntryProfile> GetGameTextEntryProfiles()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _gameTextEntryProfiles.Profiles;
    }

    public async ValueTask<IReadOnlyList<GameTextEntryProfile>> GetGameTextEntryProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _gameTextEntryProfiles.Profiles;
    }

    /// <summary>
    /// Replaces the live profile registry without writing storage. Persistence-facing
    /// callers should use SaveGameTextEntryProfileAsync/DeleteGameTextEntryProfileAsync.
    /// </summary>
    public void ReplaceGameTextEntryProfiles(
        IEnumerable<GameTextEntryProfile>? profiles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gameTextEntryProfiles.ReplaceProfiles(profiles);
        EnsureGameTextEntryHotkeyMonitorStarted();

        if (_gameTextEntryRuntimeState.GetSnapshot().Active)
        {
            RequestGameTextEntryRefresh();
        }
    }

    public RecentGameplayTarget? GetRecentGameplayTarget()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Volatile.Read(ref _recentGameplayTarget);
    }

    public RecentGameplayTarget? GetActiveGameplayTarget()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _activeGameplayTargetTracker.Current;
    }

    public async ValueTask SaveGameTextEntryProfileAsync(
        GameTextEntryProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        var next = _gameTextEntryProfiles.Profiles
            .Where(item => !StringComparer.Ordinal.Equals(item.ApplicationIdentityKey, profile.ApplicationIdentityKey))
            .Append(profile)
            .ToArray();
        var validator = new GameTextEntryProfileRegistry();
        validator.ReplaceProfiles(next);
        await _gameTextEntryProfileRepository
            .ReplaceProfilesAsync(validator.Profiles, cancellationToken)
            .ConfigureAwait(false);
        _gameTextEntryProfiles.ReplaceProfiles(validator.Profiles);
        EnsureGameTextEntryHotkeyMonitorStarted();

        var runtime = _gameTextEntryRuntimeState.GetSnapshot();
        if (runtime.Active &&
            StringComparer.Ordinal.Equals(runtime.ApplicationKey, profile.ApplicationIdentityKey) &&
            (!profile.Enabled ||
             (runtime.Source == GameTextEntryActivationSource.HotkeyProfile &&
              !profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile)) ||
             (runtime.Source == GameTextEntryActivationSource.StandardTextControl &&
              !profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.StandardTextControl))))
        {
            _gameTextEntryRuntimeState.Deactivate("profile-updated");
        }

        if (ForegroundContext.Current is { } current)
        {
            UpdateGameTextEntryHotkeyMonitorContext(current, IsAutomationEnabled);
        }
        RequestGameTextEntryRefresh();
    }

    public async ValueTask<bool> DeleteGameTextEntryProfileAsync(
        string applicationIdentityKey,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(applicationIdentityKey))
        {
            return false;
        }

        var current = _gameTextEntryProfiles.Profiles;
        var next = current
            .Where(item => !StringComparer.Ordinal.Equals(item.ApplicationIdentityKey, applicationIdentityKey))
            .ToArray();
        if (next.Length == current.Count)
        {
            return false;
        }

        await _gameTextEntryProfileRepository
            .ReplaceProfilesAsync(next, cancellationToken)
            .ConfigureAwait(false);
        _gameTextEntryProfiles.ReplaceProfiles(next);
        var runtime = _gameTextEntryRuntimeState.GetSnapshot();
        if (runtime.Active &&
            StringComparer.Ordinal.Equals(runtime.ApplicationKey, applicationIdentityKey))
        {
            _gameTextEntryRuntimeState.Deactivate("profile-deleted");
        }

        if (ForegroundContext.Current is { } foreground)
        {
            UpdateGameTextEntryHotkeyMonitorContext(foreground, IsAutomationEnabled);
        }
        RequestGameTextEntryRefresh();
        return true;
    }

    /// <summary>
    /// Activation seam for hotkey/game-specific adapters. It accepts activation only
    /// while the current resolved context is Gameplay. Standard accessibility focus
    /// detection drives the same runtime state from the context detector itself.
    /// </summary>
    public bool TryEnterGameTextEntry(
        GameTextEntryActivationSource source,
        string? profileId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = ForegroundContext.Current;
        var context = current?.Context;
        if (current is null ||
            context is null ||
            !context.HasSignal(InputContextSignalKind.Game))
        {
            return false;
        }

        _ = _gameTextEntryRuntimeState.Activate(
            context.Application,
            current.Window.ProcessId,
            current.Window.Hwnd,
            current.Window.ProcessName,
            source,
            profileId,
            reason: "adapter-enter");

        // Relax gameplay protection immediately, before the asynchronous context
        // refresh/IME decision runs. This prevents the hotkey guard or US baseline
        // from fighting a future text-entry adapter during the transition window.
        _gameplayKeyboardBaseline.UpdateContext(
            gameContext: true,
            gameTextEntry: true,
            hwnd: current.Window.Hwnd,
            focusHwnd: context.FocusHwnd,
            threadId: current.Window.ThreadId,
            processName: current.Window.ProcessName);
        _gameplayHotkeyGuard.UpdateContext(
            gameContext: true,
            gameTextEntry: true,
            processName: current.Window.ProcessName);
        _inputStatusOverlay.Hide();

        RequestGameTextEntryRefresh();
        return true;
    }

    public bool ExitGameTextEntry(string reason = "adapter-exit")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var changed = _gameTextEntryRuntimeState.Deactivate(reason);
        if (changed)
        {
            RequestGameTextEntryRefresh();
        }

        return changed;
    }

    public async ValueTask<GameplayKeyboardBaselineSettings> GetGameplayKeyboardBaselineSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = await _settingsRepository
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return settings.GameplayKeyboardBaseline;
    }

    public async ValueTask SetGameplayKeyboardBaselineSettingsAsync(
        GameplayKeyboardBaselineSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _settingsRepository
            .SaveGameplayKeyboardBaselineAsync(settings, cancellationToken)
            .ConfigureAwait(false);
        _gameplayKeyboardBaseline.UpdateSettings(settings);
        _gameplayKeyboardBaseline.SetAutomationEnabled(IsAutomationEnabled);

        if (ForegroundContext.Current is { } current)
        {
            UpdateGameplayKeyboardBaselineContext(current);
        }

        if (IsAutomationEnabled)
        {
            _automation.RequestCurrentReapply(InputContextTrigger.ManualRefresh);
        }
    }

    public GameplayKeyboardBaselineSnapshot GetGameplayKeyboardBaselineSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _gameplayKeyboardBaseline.GetSnapshot();
    }

    public async ValueTask<GameplayHotkeyGuardSettings> GetGameplayHotkeyGuardSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = await _settingsRepository
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return settings.GameplayHotkeyGuard;
    }

    public async ValueTask SetGameplayHotkeyGuardSettingsAsync(
        GameplayHotkeyGuardSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _settingsRepository
            .SaveGameplayHotkeyGuardAsync(settings, cancellationToken)
            .ConfigureAwait(false);
        _gameplayHotkeyGuard.UpdateSettings(settings);
    }

    public GameplayHotkeyGuardSnapshot GetGameplayHotkeyGuardSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _gameplayHotkeyGuard.GetSnapshot();
    }

    public async ValueTask<InputStatusOverlaySettings> GetInputStatusOverlaySettingsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = await _settingsRepository
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return settings.InputStatusOverlay;
    }

    public async ValueTask SetInputStatusOverlaySettingsAsync(
        InputStatusOverlaySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var normalized = settings.Normalize();
        await _settingsRepository
            .SaveInputStatusOverlayAsync(normalized, cancellationToken)
            .ConfigureAwait(false);
        _inputStatusOverlay.UpdateSettings(normalized);
    }

    public InputStatusOverlaySnapshot GetInputStatusOverlaySnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _inputStatusOverlay.GetSnapshot();
    }

    public IReadOnlyList<AutomationDecisionRecord> GetRecentAutomationDecisions(
        int maxCount = 20)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _automation.GetRecentDecisions(maxCount);
    }

    /// <summary>
    /// Called by the resident tray HWND after Windows resumes or the user session
    /// is unlocked. Multiple native notifications collapse into one delayed refresh.
    /// The P1 WinEvent hooks stay registered; this only re-samples the actual current
    /// foreground once the desktop has had time to settle.
    /// </summary>
    public void RequestSystemRecovery(string reason)
    {
        if (_disposed)
        {
            return;
        }

        CancellationTokenSource? previousCancellation;
        Task previousTask;
        var cancellation = new CancellationTokenSource();

        lock (_recoverySync)
        {
            if (_disposed)
            {
                cancellation.Dispose();
                return;
            }

            previousCancellation = _recoveryCancellation;
            previousTask = _recoveryTask;
            previousCancellation?.Cancel();

            _recoveryCancellation = cancellation;
            _recoveryTask = RecoverAfterSystemTransitionAsync(reason, cancellation.Token);
        }

        if (previousCancellation is not null)
        {
            _ = DisposeRecoveryCancellationWhenSafeAsync(
                previousCancellation,
                previousTask);
        }
    }

    public async ValueTask<string> CreateDiagnosticsReportAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var configuration = await _ruleRepository
            .GetConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);
        var rules = configuration.Rules;
        var globalDefault = configuration.GlobalDefault;
        var repository = _ruleRepository.Diagnostics;
        var automation = _automation.GetRuntimeSnapshot();
        var automationLog = _automationTraceListener?.GetSnapshot();
        var recentDecisions = _automation.GetRecentDecisions(8);
        var recentGameplay = _gameplayEligibilityDetector.GetRecentObservations(8);
        var gameplayKeyboard = _gameplayKeyboardBaseline.GetSnapshot();
        var hotkeyGuard = _gameplayHotkeyGuard.GetSnapshot();
        var textEntryHotkeys = _gameTextEntryHotkeyMonitor.GetSnapshot();
        var overlay = _inputStatusOverlay.GetSnapshot();
        var gameTextEntry = _gameTextEntryRuntimeState.GetSnapshot();
        var gameTextEntryProfileCount = _gameTextEntryProfiles.Profiles.Count;
        var recentGameplayTarget = Volatile.Read(ref _recentGameplayTarget);
        long gameplayExitRestoreCount;
        DateTimeOffset? gameplayExitRestoreLastAt;
        string gameplayExitRestoreLastTarget;
        string gameplayExitRestoreLastResult;
        lock (_gameplayExitRestoreSync)
        {
            gameplayExitRestoreCount = _gameplayExitRestoreCount;
            gameplayExitRestoreLastAt = _gameplayExitRestoreLastAt;
            gameplayExitRestoreLastTarget = _gameplayExitRestoreLastTarget;
            gameplayExitRestoreLastResult = _gameplayExitRestoreLastResult;
        }

        var current = ForegroundContext.Current;
        nint currentKeyboardLayout = 0;
        if (current is not null)
        {
            try
            {
                currentKeyboardLayout = _keyboardLayoutInspector.GetKeyboardLayout(current.Window.ThreadId);
            }
            catch
            {
                currentKeyboardLayout = current.Input.KeyboardLayout;
            }
        }

        var currentKeyboardLayoutText = currentKeyboardLayout == 0
            ? "none"
            : $"0x{unchecked((uint)(nuint)currentKeyboardLayout):X8}";

        bool? startupEnabled;
        try
        {
            startupEnabled = StartupRegistration.IsEnabled;
        }
        catch
        {
            startupEnabled = null;
        }

        var assemblyVersion = Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version?.ToString() ?? "unknown";

        var builder = new StringBuilder();
        builder.AppendLine("FlowIME diagnostics");
        builder.AppendLine($"utc={DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"version={assemblyVersion}");
        builder.AppendLine($"os={RuntimeInformation.OSDescription}");
        builder.AppendLine($"framework={RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"osArchitecture={RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"processArchitecture={RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"processId={Environment.ProcessId}");
        builder.AppendLine($"automationEnabled={IsAutomationEnabled}");
        builder.AppendLine($"automationExecutionGate={automation.ExecutionEnabled}");
        builder.AppendLine($"automationStarted={automation.Started}");
        builder.AppendLine($"automationGeneration={automation.Generation}");
        builder.AppendLine($"automationInFlight={automation.OperationInFlight}");
        builder.AppendLine($"automationLastErrorType={automation.LastErrorType ?? "none"}");
        builder.AppendLine($"automationLastError={SanitizeDiagnosticValue(automation.LastErrorMessage)}");
        builder.AppendLine($"automationLogSinkEnabled={automationLog?.Enabled ?? false}");
        builder.AppendLine($"automationLogEntryCount={automationLog?.EntryCount ?? 0}");
        builder.AppendLine($"automationLogWrittenBytes={automationLog?.WrittenBytes ?? 0}");
        builder.AppendLine($"automationLogPendingBytes={automationLog?.PendingBytes ?? 0}");
        builder.AppendLine($"automationLogFlushCount={automationLog?.FlushCount ?? 0}");
        builder.AppendLine($"automationLogRotationCount={automationLog?.RotationCount ?? 0}");
        builder.AppendLine($"automationLogLastErrorType={automationLog?.LastErrorType ?? "none"}");
        builder.AppendLine($"decisionJournalCount={automation.DecisionJournalCount}");
        builder.AppendLine($"lastDecisionOutcome={automation.LastDecisionOutcome ?? "none"}");
        builder.AppendLine($"lastDecisionReason={automation.LastDecisionReason ?? "none"}");
        builder.AppendLine($"gameplayKeyboardBaselineEnabled={gameplayKeyboard.Settings.Enabled}");
        builder.AppendLine($"gameplayKeyboardBaselineAutomationEnabled={gameplayKeyboard.AutomationEnabled}");
        builder.AppendLine($"gameplayKeyboardBaselineUsAvailable={gameplayKeyboard.UsKeyboardAvailable}");
        builder.AppendLine($"gameplayKeyboardBaselineTarget={gameplayKeyboard.TargetLayout}");
        builder.AppendLine($"gameplayKeyboardBaselineLastObserved={gameplayKeyboard.LastObservedLayout}");
        builder.AppendLine($"gameplayKeyboardBaselineActive={gameplayKeyboard.Active}");
        builder.AppendLine($"gameplayKeyboardBaselineGameContext={gameplayKeyboard.GameContext}");
        builder.AppendLine($"gameplayKeyboardBaselineGameTextEntry={gameplayKeyboard.GameTextEntry}");
        builder.AppendLine($"gameplayKeyboardBaselineOutcome={gameplayKeyboard.LastOutcome}");
        builder.AppendLine($"gameplayKeyboardBaselineError={SanitizeDiagnosticValue(gameplayKeyboard.LastError)}");
        builder.AppendLine($"gameplayKeyboardBaselineProcess={SanitizeDiagnosticToken(gameplayKeyboard.ProcessName)}");
        builder.AppendLine($"gameplayKeyboardBaselineAppliedCount={gameplayKeyboard.AppliedCount}");
        builder.AppendLine($"gameplayKeyboardBaselineLastAttemptAt={gameplayKeyboard.LastAttemptAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"gameplayKeyboardBaselineLastAppliedAt={gameplayKeyboard.LastAppliedAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"gameplayHotkeyGuardInstalled={hotkeyGuard.Installed}");
        builder.AppendLine($"gameplayHotkeyGuardArmed={hotkeyGuard.Armed}");
        builder.AppendLine($"gameplayHotkeyGuardEnabled={hotkeyGuard.Settings.Enabled}");
        builder.AppendLine($"gameplayHotkeyGuardBlockWinSpace={hotkeyGuard.Settings.BlockWinSpace}");
        builder.AppendLine($"gameplayHotkeyGuardBlockCtrlSpace={hotkeyGuard.Settings.BlockCtrlSpace}");
        builder.AppendLine($"gameplayHotkeyGuardBlockLegacy={hotkeyGuard.Settings.BlockLegacyLanguageHotkeys}");
        builder.AppendLine($"gameplayHotkeyGuardGameContext={hotkeyGuard.GameContext}");
        builder.AppendLine($"gameplayHotkeyGuardGameTextEntry={hotkeyGuard.GameTextEntry}");
        builder.AppendLine($"gameplayHotkeySuppressedCount={hotkeyGuard.SuppressedCount}");
        builder.AppendLine($"gameplayHotkeyLastSuppressed={hotkeyGuard.LastSuppressedChord}");
        builder.AppendLine($"gameplayHotkeyLastSuppressedAt={hotkeyGuard.LastSuppressedAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"gameplayHotkeyLastSuppressedProcess={SanitizeDiagnosticToken(hotkeyGuard.LastSuppressedProcessName)}");
        builder.AppendLine($"gameplayHotkeyGuardProcess={SanitizeDiagnosticToken(hotkeyGuard.ProcessName)}");
        builder.AppendLine($"gameplayHotkeyGuardError={SanitizeDiagnosticValue(hotkeyGuard.LastError)}");
        builder.AppendLine($"gameTextEntryHotkeyMonitorInstalled={textEntryHotkeys.Installed}");
        builder.AppendLine($"gameTextEntryHotkeyMonitorArmed={textEntryHotkeys.Armed}");
        builder.AppendLine($"gameTextEntryHotkeyMonitorGameContext={textEntryHotkeys.GameContext}");
        builder.AppendLine($"gameTextEntryHotkeyMonitorGameTextEntry={textEntryHotkeys.GameTextEntry}");
        builder.AppendLine($"gameTextEntryHotkeyMonitorProfile={SanitizeDiagnosticToken(textEntryHotkeys.ProfileId)}");
        builder.AppendLine($"gameTextEntryHotkeyEnterDetectedCount={textEntryHotkeys.EnterDetectedCount}");
        builder.AppendLine($"gameTextEntryHotkeyExitDetectedCount={textEntryHotkeys.ExitDetectedCount}");
        builder.AppendLine($"gameTextEntryHotkeyLastGesture={SanitizeDiagnosticToken(textEntryHotkeys.LastGesture)}");
        builder.AppendLine($"gameTextEntryHotkeyLastDetectedAt={textEntryHotkeys.LastDetectedAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"gameTextEntryHotkeyMonitorError={SanitizeDiagnosticValue(textEntryHotkeys.LastError)}");
        builder.AppendLine($"recentGameplayTarget={SanitizeDiagnosticToken(recentGameplayTarget?.ProcessName)}");
        builder.AppendLine($"gameplayExitRestoreCount={gameplayExitRestoreCount}");
        builder.AppendLine($"gameplayExitRestoreLastAt={gameplayExitRestoreLastAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"gameplayExitRestoreLastTarget={SanitizeDiagnosticToken(gameplayExitRestoreLastTarget)}");
        builder.AppendLine($"gameplayExitRestoreLastResult={SanitizeDiagnosticToken(gameplayExitRestoreLastResult)}");
        builder.AppendLine($"inputStatusOverlayEnabled={overlay.Settings.Enabled}");
        builder.AppendLine($"inputStatusOverlayStarted={overlay.Started}");
        builder.AppendLine($"inputStatusOverlayVisible={overlay.Visible}");
        builder.AppendLine($"inputStatusOverlayPersistent={overlay.Persistent}");
        builder.AppendLine($"inputStatusOverlayLabel={SanitizeDiagnosticToken(overlay.Label)}");
        builder.AppendLine($"inputStatusOverlayShowCount={overlay.ShowCount}");
        builder.AppendLine($"inputStatusOverlayLastShownAt={overlay.LastShownAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"inputStatusOverlayError={SanitizeDiagnosticValue(overlay.LastError)}");
        builder.AppendLine($"gameTextEntryActive={gameTextEntry.Active}");
        builder.AppendLine($"gameTextEntryGeneration={gameTextEntry.Generation}");
        builder.AppendLine($"gameTextEntrySource={gameTextEntry.Source}");
        builder.AppendLine($"gameTextEntryProfile={SanitizeDiagnosticToken(gameTextEntry.ProfileId)}");
        builder.AppendLine($"gameTextEntryProcess={SanitizeDiagnosticToken(gameTextEntry.ProcessName)}");
        builder.AppendLine($"gameTextEntryActivatedAt={gameTextEntry.ActivatedAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"gameTextEntryLastChangedAt={gameTextEntry.LastChangedAt?.ToString("O") ?? "none"}");
        builder.AppendLine($"gameTextEntryLastReason={SanitizeDiagnosticToken(gameTextEntry.LastReason)}");
        builder.AppendLine($"gameTextEntryProfileCount={gameTextEntryProfileCount}");
        builder.AppendLine($"recentDecisionCount={recentDecisions.Count}");
        for (var index = 0; index < recentDecisions.Count; index++)
        {
            var record = recentDecisions[index];
            builder.AppendLine(
                $"recentDecision[{index}]={record.Timestamp:O}|" +
                $"{record.ProcessName}|{FormatContextSignalKinds(record.Signals)}|" +
                $"{record.Trigger}|{record.DecisionSource}|{record.ReasonCode}|{record.Outcome}|" +
                $"provider={SanitizeDiagnosticToken(record.ProviderId ?? "none")}|" +
                $"action={record.Action}|" +
                $"thread={record.TargetThreadId}|" +
                $"backend={SanitizeDiagnosticToken(record.Backend)}|" +
                $"beforeProfile={SanitizeDiagnosticToken(record.BeforeState?.ProfileName)}|" +
                $"beforeMode={record.BeforeState?.Mode.ToString() ?? "unknown"}|" +
                $"beforeHkl={FormatKeyboardLayout(record.BeforeState?.KeyboardLayout)}|" +
                $"afterProfile={SanitizeDiagnosticToken(record.AfterState?.ProfileName)}|" +
                $"afterMode={record.AfterState?.Mode.ToString() ?? "unknown"}|" +
                $"afterHkl={FormatKeyboardLayout(record.AfterState?.KeyboardLayout)}|" +
                $"durationMs={record.Duration?.TotalMilliseconds.ToString("F1") ?? "none"}|" +
                $"error={SanitizeDiagnosticToken(record.ErrorCode ?? "none")}");
        }
        builder.AppendLine($"recentGameplayCount={recentGameplay.Count}");
        for (var index = 0; index < recentGameplay.Count; index++)
        {
            var observation = recentGameplay[index];
            builder.AppendLine(
                $"recentGameplay[{index}]={observation.Timestamp:O}|" +
                $"{observation.ProcessName}|fullscreen={observation.IsFullscreen}|" +
                $"eligible={observation.IsEligible}|confidence={observation.Confidence}|" +
                $"reason={SanitizeDiagnosticToken(observation.Reason)}|" +
                $"evidence={FormatGameplayEvidence(observation.Evidence)}");
        }
        builder.AppendLine($"startupRegistered={FormatNullable(startupEnabled)}");
        var ruleDiagnostics = RuleSetAnalyzer.Analyze(rules);
        builder.AppendLine($"rulesTotal={rules.Count}");
        builder.AppendLine($"rulesEnabled={rules.Count(rule => rule.Enabled)}");
        builder.AppendLine($"ruleExactOverlapGroups={ruleDiagnostics.ExactOverlapGroupCount}");
        builder.AppendLine($"ruleConflictingTargetGroups={ruleDiagnostics.ConflictingTargetGroupCount}");
        builder.AppendLine($"ruleRedundantTargetGroups={ruleDiagnostics.RedundantTargetGroupCount}");
        builder.AppendLine($"ruleAffectedCount={ruleDiagnostics.AffectedRuleCount}");
        builder.AppendLine($"globalDefaultEnabled={globalDefault is not null}");
        builder.AppendLine($"globalDefaultProvider={globalDefault?.ProviderId ?? "none"}");
        builder.AppendLine($"globalDefaultAction={globalDefault?.Action.ToString() ?? "none"}");
        builder.AppendLine($"ruleRepositoryState={repository.State}");
        builder.AppendLine($"ruleRepositoryDetail={repository.Detail ?? "none"}");
        builder.AppendLine($"ruleCorruptArtifact={FormatFileName(repository.CorruptArtifactPath)}");
        builder.AppendLine($"currentProcess={current?.Window.ProcessName ?? "none"}");
        builder.AppendLine($"currentApplicationIdentity={current?.Context?.Application.Key ?? "none"}");
        builder.AppendLine($"currentContextSignals={FormatContextSignals(current?.Context)}");
        builder.AppendLine($"currentContextSignalDetails={FormatContextSignalDetails(current?.Context)}");
        builder.AppendLine($"currentDecisionSource={current?.Decision?.Source.ToString() ?? "None"}");
        builder.AppendLine($"currentDecisionReason={current?.Decision?.Reason ?? "none"}");
        builder.AppendLine($"currentProfile={current?.Input.ProfileName ?? "unknown"}");
        builder.AppendLine($"currentMode={current?.Input.Mode.ToString() ?? "Unknown"}");
        builder.AppendLine($"currentKeyboardLayout={currentKeyboardLayoutText}");
        builder.AppendLine($"currentRule={current?.MatchedRule?.Id.ToString() ?? "none"}");
        var currentRuleProvider = current?.MatchedRule is { } currentRule
            ? FlowIME.Core.Models.InputMethodProviderIds.Normalize(currentRule.ProviderId)
            : "none";
        builder.AppendLine($"currentRuleProvider={currentRuleProvider}");
        builder.AppendLine($"currentResolutionSource={current?.ResolutionSource.ToString() ?? "None"}");
        builder.AppendLine($"currentResolvedProvider={current?.MatchedProviderId ?? "none"}");
        builder.AppendLine($"inputProviderDefault={_inputMethodProviders.DefaultProvider.Descriptor.Id}");
        builder.AppendLine($"inputProviderCount={_inputMethodProviders.Descriptors.Count}");
        foreach (var descriptor in _inputMethodProviders.Descriptors)
        {
            builder.AppendLine(
                $"inputProvider[{descriptor.Id}]={descriptor.DisplayName}|" +
                FormatProviderCapabilities(descriptor.Capabilities));
        }
        builder.AppendLine($"rulesFileExists={File.Exists(_paths.RulesFilePath)}");
        builder.AppendLine($"rulesBackupExists={File.Exists(_paths.RulesBackupFilePath)}");
        builder.AppendLine($"settingsFileExists={File.Exists(_paths.SettingsFilePath)}");
        builder.AppendLine($"gameTextEntryProfilesFileExists={File.Exists(_paths.GameTextEntryProfilesFilePath)}");
        builder.AppendLine($"automationLog={FormatLogFile(_paths.AutomationLogPath)}");
        builder.AppendLine($"automationLog1={FormatLogFile(_paths.AutomationLogPath + ".1")}");
        builder.AppendLine($"automationLog2={FormatLogFile(_paths.AutomationLogPath + ".2")}");
        builder.AppendLine($"automationLog3={FormatLogFile(_paths.AutomationLogPath + ".3")}");
        return builder.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancellationTokenSource? recoveryCancellation;
        Task recoveryTask;
        lock (_recoverySync)
        {
            recoveryCancellation = _recoveryCancellation;
            recoveryTask = _recoveryTask;
            _recoveryCancellation = null;
            _recoveryTask = Task.CompletedTask;
            recoveryCancellation?.Cancel();
        }

        if (recoveryCancellation is not null)
        {
            try
            {
                await recoveryTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                recoveryCancellation.Dispose();
            }
        }

        CancellationTokenSource? gameplayExitCancellation;
        Task gameplayExitTask;
        lock (_gameplayExitRestoreSync)
        {
            gameplayExitCancellation = _gameplayExitRestoreCancellation;
            gameplayExitTask = _gameplayExitRestoreTask;
            _gameplayExitRestoreCancellation = null;
            _gameplayExitRestoreTask = Task.CompletedTask;
            gameplayExitCancellation?.Cancel();
        }

        if (gameplayExitCancellation is not null)
        {
            try
            {
                await gameplayExitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                gameplayExitCancellation.Dispose();
            }
        }

        CancellationTokenSource? textEntryHotkeyCancellation;
        Task textEntryHotkeyTask;
        lock (_gameTextEntryHotkeySync)
        {
            textEntryHotkeyCancellation = _gameTextEntryHotkeyCancellation;
            textEntryHotkeyTask = _gameTextEntryHotkeyTask;
            _gameTextEntryHotkeyCancellation = null;
            _gameTextEntryHotkeyTask = Task.CompletedTask;
            textEntryHotkeyCancellation?.Cancel();
        }

        if (textEntryHotkeyCancellation is not null)
        {
            try
            {
                await textEntryHotkeyTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                textEntryHotkeyCancellation.Dispose();
            }
        }

        ForegroundContext.StateChanged -= OnForegroundContextStateChanged;
        _foregroundSource.ForegroundWindowChanged -= OnRawForegroundChangedForHotkeyGuard;
        _automation.DecisionRecorded -= OnAutomationDecisionRecorded;
        _gameTextEntryHotkeyMonitor.TransitionDetected -= OnGameTextEntryHotkeyTransitionDetected;
        await _initializationTask.ConfigureAwait(false);
        _gameplayHotkeyGuard.SetAutomationEnabled(false);
        _gameplayHotkeyGuard.Dispose();
        _gameTextEntryHotkeyMonitor.Dispose();
        _inputStatusOverlay.Dispose();
        await _automation.DisposeAsync().ConfigureAwait(false);
        await ForegroundContext.DisposeAsync().ConfigureAwait(false);
        _foregroundSource.Dispose();
        _settingsRepository.Dispose();
        _gameTextEntryProfileRepository.Dispose();
        _ruleRepository.Dispose();

        if (_automationTraceListener is not null)
        {
            Trace.WriteLine(
                $"[FlowIME.Automation] stage=session-stop utc={DateTimeOffset.UtcNow:O}");
            Trace.Listeners.Remove(_automationTraceListener);
            _automationTraceListener.Flush();
            _automationTraceListener.Dispose();
        }
    }

    private async Task InitializeServicesAsync()
    {
        await Task.WhenAll(
                InitializeRuleRepositoryAsync(),
                InitializeAppSettingsAsync(),
                InitializeGameTextEntryProfilesAsync())
            .ConfigureAwait(false);
    }

    private async Task InitializeRuleRepositoryAsync()
    {
        try
        {
            _ = await _ruleRepository.GetRulesAsync().ConfigureAwait(false);
            var diagnostics = _ruleRepository.Diagnostics;
            Trace.WriteLine(
                $"[FlowIME.Configuration] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=startup-load state={diagnostics.State} " +
                $"detail={diagnostics.Detail ?? "none"}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Configuration] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=startup-load result=failed type={ex.GetType().Name}");
        }
    }

    private async Task InitializeGameTextEntryProfilesAsync()
    {
        try
        {
            var profiles = await _gameTextEntryProfileRepository
                .GetProfilesAsync()
                .ConfigureAwait(false);
            _gameTextEntryProfiles.ReplaceProfiles(profiles);
            EnsureGameTextEntryHotkeyMonitorStarted();

            if (ForegroundContext.Current is { } current)
            {
                UpdateGameTextEntryHotkeyMonitorContext(current, IsAutomationEnabled);
            }

            if (IsAutomationEnabled)
            {
                _automation.RequestCurrentReapply(InputContextTrigger.GameTextEntryChanged);
            }

            Trace.WriteLine(
                $"[FlowIME.GameTextEntry] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=profiles-load count={profiles.Count}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameTextEntry] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=profiles-load result=failed type={ex.GetType().Name}");
        }
    }

    private async Task InitializeAppSettingsAsync()
    {
        try
        {
            var settings = await _settingsRepository.GetAsync().ConfigureAwait(false);
            _gameplayKeyboardBaseline.UpdateSettings(settings.GameplayKeyboardBaseline);
            _gameplayKeyboardBaseline.SetAutomationEnabled(IsAutomationEnabled);
            _gameplayHotkeyGuard.UpdateSettings(settings.GameplayHotkeyGuard);
            _gameplayHotkeyGuard.SetAutomationEnabled(IsAutomationEnabled);
            _inputStatusOverlay.UpdateSettings(settings.InputStatusOverlay);

            if (ForegroundContext.Current is { } current)
            {
                UpdateGameplayKeyboardBaselineContext(current);
                UpdateGameplayHotkeyGuardContext(current);
                UpdateGameTextEntryHotkeyMonitorContext(current, IsAutomationEnabled);
                UpdateInputStatusOverlay(current);
            }

            if (IsAutomationEnabled)
            {
                _automation.RequestCurrentReapply(InputContextTrigger.ManualRefresh);
            }

            var baseline = _gameplayKeyboardBaseline.GetSnapshot();
            Trace.WriteLine(
                $"[FlowIME.GameplayKeyboard] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=settings-load enabled={settings.GameplayKeyboardBaseline.Enabled} " +
                $"usAvailable={baseline.UsKeyboardAvailable}");
            Trace.WriteLine(
                $"[FlowIME.GameplayHotkey] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=settings-load enabled={settings.GameplayHotkeyGuard.Enabled} " +
                $"winSpace={settings.GameplayHotkeyGuard.BlockWinSpace} " +
                $"ctrlSpace={settings.GameplayHotkeyGuard.BlockCtrlSpace} " +
                $"legacy={settings.GameplayHotkeyGuard.BlockLegacyLanguageHotkeys}");
            Trace.WriteLine(
                $"[FlowIME.Overlay] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=settings-load enabled={settings.InputStatusOverlay.Enabled}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameplayHotkey] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=settings-load result=failed type={ex.GetType().Name}");
        }
    }

    private void TryStartGameplayHotkeyGuard()
    {
        try
        {
            _gameplayHotkeyGuard.Start();
        }
        catch (Exception ex)
        {
            // Hotkey protection is an additive safety feature. Failure to install
            // the low-level hook must not prevent FlowIME's existing automation
            // and rule engine from starting. Diagnostics expose the failure.
            Trace.WriteLine(
                $"[FlowIME.GameplayHotkey] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=hook-install result=failed type={ex.GetType().Name}");
        }
    }

    private void EnsureGameTextEntryHotkeyMonitorStarted()
    {
        if (_gameTextEntryProfiles.Profiles.Any(profile =>
                profile.Enabled &&
                profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile)))
        {
            TryStartGameTextEntryHotkeyMonitor();
        }
    }

    private void TryStartGameTextEntryHotkeyMonitor()
    {
        try
        {
            _gameTextEntryHotkeyMonitor.Start();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.GameTextEntry] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=hotkey-hook-install result=failed type={ex.GetType().Name}");
        }
    }

    private void TryStartInputStatusOverlay()
    {
        try
        {
            _inputStatusOverlay.Start();
        }
        catch (Exception ex)
        {
            // The indicator is optional feedback. Overlay initialization must never
            // block the automation engine, gameplay protection or tray startup.
            Trace.WriteLine(
                $"[FlowIME.Overlay] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=window-init result=failed type={ex.GetType().Name}");
        }
    }

    private void OnRawForegroundChangedForHotkeyGuard(
        object? sender,
        ForegroundWindowChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var textEntry = _gameTextEntryRuntimeState.GetSnapshot();
            if (textEntry.Active && textEntry.TopLevelHwnd != args.Hwnd)
            {
                _gameTextEntryRuntimeState.Deactivate("foreground-changed");
            }

            // Disarm immediately on every top-level foreground transition. The richer
            // context pipeline re-arms the guard only after the new foreground is
            // positively classified as Gameplay. This also prevents stale Game
            // state from leaking into FlowIME's own window, which ForegroundContext
            // intentionally ignores.
            _gameplayKeyboardBaseline.UpdateContext(
                gameContext: false,
                gameTextEntry: false,
                hwnd: 0,
                focusHwnd: 0,
                threadId: 0,
                processName: "pending");
            _gameplayHotkeyGuard.UpdateContext(
                gameContext: false,
                gameTextEntry: false,
                processName: "pending");
            _gameTextEntryHotkeyMonitor.UpdateContext(
                IsAutomationEnabled,
                gameContext: false,
                gameTextEntry: false,
                profile: null,
                processName: "pending");
            _inputStatusOverlay.Hide();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnGameTextEntryHotkeyTransitionDetected(
        GameTextEntryHotkeyTransitionEvent transition)
    {
        if (_disposed)
        {
            return;
        }

        CancellationTokenSource? previous;
        Task previousTask;
        var cancellation = new CancellationTokenSource();
        lock (_gameTextEntryHotkeySync)
        {
            previous = _gameTextEntryHotkeyCancellation;
            previousTask = _gameTextEntryHotkeyTask;
            previous?.Cancel();
            _gameTextEntryHotkeyCancellation = cancellation;
            _gameTextEntryHotkeyTask = ProcessGameTextEntryHotkeyTransitionAsync(
                transition,
                cancellation.Token);
        }

        if (previous is not null)
        {
            _ = DisposeCancellationWhenSafeAsync(previous, previousTask);
        }
    }

    private async Task ProcessGameTextEntryHotkeyTransitionAsync(
        GameTextEntryHotkeyTransitionEvent transition,
        CancellationToken cancellationToken)
    {
        var delay = transition.Transition == GameTextEntryHotkeyTransition.Enter
            ? GameTextEntryHotkeyEnterDelay
            : GameTextEntryHotkeyExitDelay;
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        var current = ForegroundContext.Current;
        var context = current?.Context;
        if (current is null ||
            context is null ||
            !context.HasSignal(InputContextSignalKind.Game) ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                current.Window.ProcessName,
                transition.ProcessName))
        {
            return;
        }

        var profile = _gameTextEntryProfiles.Resolve(context.Application);
        if (profile is null ||
            !profile.DetectionMode.HasFlag(GameTextEntryDetectionMode.HotkeyProfile) ||
            !StringComparer.Ordinal.Equals(profile.Id, transition.ProfileId))
        {
            return;
        }

        if (transition.Transition == GameTextEntryHotkeyTransition.Enter)
        {
            _ = TryEnterGameTextEntry(
                GameTextEntryActivationSource.HotkeyProfile,
                profile.Id);
        }
        else
        {
            _ = ExitGameTextEntry("hotkey-profile-exit");
        }
    }

    private static async Task DisposeCancellationWhenSafeAsync(
        CancellationTokenSource cancellation,
        Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void OnAutomationDecisionRecorded(AutomationDecisionRecord record)
    {
        if (_disposed || record.Outcome != AutomationDecisionOutcome.Applied)
        {
            return;
        }

        // Re-sample after the provider reports success so Home/overlay reflect the
        // state that was actually applied rather than the pre-mutation foreground
        // sample that triggered automation. This is observation-only and does not
        // schedule another automation decision.
        ForegroundContext.RequestCurrentRefresh(
            record.Trigger is InputContextTrigger.GameplayExit or InputContextTrigger.GameTextEntryChanged
                ? record.Trigger
                : InputContextTrigger.ManualRefresh);
    }

    private void OnForegroundContextStateChanged(CurrentStateSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var isGameplay = snapshot.Context?.HasSignal(InputContextSignalKind.Game) == true;
            var detectedNewTarget = UpdateActiveGameplayTarget(snapshot, isGameplay);

            if (!isGameplay)
            {
                _gameTextEntryRuntimeState.Deactivate("gameplay-not-active");
            }

            var transition = _gameplayTransitionTracker.Update(isGameplay);

            UpdateGameplayKeyboardBaselineContext(snapshot);
            UpdateGameplayHotkeyGuardContext(snapshot);
            UpdateGameTextEntryHotkeyMonitorContext(snapshot, IsAutomationEnabled);

            if (transition == GameplayTransition.Exited)
            {
                // The gameplay US keyboard can become the session's active layout.
                // Do not let that state leak into the next desktop app: wait until
                // the new foreground/focus settles, then force one fresh rule pass.
                _inputStatusOverlay.Hide();
                ScheduleGameplayExitRestore(snapshot.Window.ProcessName);
            }
            else
            {
                if (transition == GameplayTransition.Entered)
                {
                    CancelGameplayExitRestore();
                }

                UpdateInputStatusOverlay(snapshot);
                if (detectedNewTarget)
                {
                    ShowGameplayDetectedOverlay(snapshot);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool UpdateActiveGameplayTarget(CurrentStateSnapshot snapshot, bool isGameplay)
    {
        RecentGameplayTarget? next = null;
        if (isGameplay && snapshot.Context is { } gameplayContext)
        {
            next = new RecentGameplayTarget(
                gameplayContext.Application.Key,
                snapshot.Window.ProcessName,
                DateTimeOffset.UtcNow,
                snapshot.Window.ExecutablePath);
            Volatile.Write(ref _recentGameplayTarget, next);
        }

        var transition = _activeGameplayTargetTracker.Update(next);
        if (!transition.Changed)
        {
            return false;
        }

        ActiveGameplayTargetChanged?.Invoke(transition.ActiveTarget);
        return transition.Entered;
    }

    private void ShowGameplayDetectedOverlay(CurrentStateSnapshot snapshot) =>
        _inputStatusOverlay.Show(
            "游戏已识别",
            snapshot.Window.Hwnd,
            persistent: false,
            duration: TimeSpan.FromMilliseconds(2400),
            focusHwnd: snapshot.Context?.FocusHwnd ?? 0);

    private void RequestGameTextEntryRefresh()
    {
        if (IsAutomationEnabled)
        {
            _automation.RequestCurrentReapply(InputContextTrigger.GameTextEntryChanged);
        }

        ForegroundContext.RequestCurrentRefresh(InputContextTrigger.GameTextEntryChanged);
    }

    private void UpdateGameplayKeyboardBaselineContext(CurrentStateSnapshot snapshot)
    {
        var context = snapshot.Context;
        _gameplayKeyboardBaseline.UpdateContext(
            gameContext: context?.HasSignal(InputContextSignalKind.Game) == true,
            gameTextEntry: context?.HasSignal(InputContextSignalKind.GameTextEntry) == true,
            hwnd: snapshot.Window.Hwnd,
            focusHwnd: context?.FocusHwnd ?? 0,
            threadId: snapshot.Window.ThreadId,
            processName: snapshot.Window.ProcessName);
    }

    private void UpdateGameplayHotkeyGuardContext(CurrentStateSnapshot snapshot)
    {
        var context = snapshot.Context;
        _gameplayHotkeyGuard.UpdateContext(
            gameContext: context?.HasSignal(InputContextSignalKind.Game) == true,
            gameTextEntry: context?.HasSignal(InputContextSignalKind.GameTextEntry) == true,
            processName: snapshot.Window.ProcessName);
    }

    private void UpdateGameTextEntryHotkeyMonitorContext(
        CurrentStateSnapshot snapshot,
        bool automationEnabled)
    {
        var context = snapshot.Context;
        var isGameplay = context?.HasSignal(InputContextSignalKind.Game) == true;
        var isGameTextEntry = context?.HasSignal(InputContextSignalKind.GameTextEntry) == true;
        var profile = context is null ? null : _gameTextEntryProfiles.Resolve(context.Application);
        _gameTextEntryHotkeyMonitor.UpdateContext(
            automationEnabled,
            isGameplay,
            isGameTextEntry,
            profile,
            snapshot.Window.ProcessName);
    }

    private void UpdateInputStatusOverlay(CurrentStateSnapshot snapshot)
    {
        var overlay = _inputStatusOverlay.GetSnapshot();
        if (!overlay.Settings.Enabled)
        {
            return;
        }

        nint keyboardLayout;
        try
        {
            keyboardLayout = _keyboardLayoutInspector.GetKeyboardLayout(snapshot.Window.ThreadId);
        }
        catch
        {
            keyboardLayout = snapshot.Input.KeyboardLayout;
        }

        var isGameplay = snapshot.Context?.HasSignal(InputContextSignalKind.Game) == true;
        var isGameTextEntry = snapshot.Context?.HasSignal(InputContextSignalKind.GameTextEntry) == true;
        var baseline = _gameplayKeyboardBaseline.GetSnapshot();

        // During Gameplay, only claim US after the target thread actually reports
        // the standard US HKL. A merely posted request must not create a false
        // positive indicator. Outside Gameplay, provider mode remains authoritative
        // and pure US is used as the fallback when no registered IME is active.
        string? label;
        if (isGameplay &&
            !isGameTextEntry &&
            baseline.Settings.Enabled &&
            baseline.UsKeyboardAvailable)
        {
            label = GameplayKeyboardBaseline.IsStandardUsKeyboard(keyboardLayout)
                ? "US"
                : null;
        }
        else
        {
            label = InputStatusOverlayStateResolver.ResolveLabel(
                snapshot.Input.Mode,
                keyboardLayout,
                gameplayUsBaselineActive: false);
        }

        if (label is null)
        {
            if (isGameplay)
            {
                _inputStatusOverlay.Hide();
            }

            return;
        }

        TsfProfileSnapshot? inputProfile = null;
        try
        {
            var detectedProfile = _inputProfileInspector.GetActiveKeyboardProfile();
            if (detectedProfile.Success)
            {
                inputProfile = detectedProfile;
            }
        }
        catch
        {
            // The overlay remains usable without an icon when TSF profile inspection fails.
        }

        _inputStatusOverlay.Show(
            label,
            snapshot.Window.Hwnd,
            // A permanent topmost indicator is disruptive in games and can outlive
            // stale gameplay/caret evidence. Every overlay presentation is bounded.
            persistent: false,
            focusHwnd: snapshot.Context?.FocusHwnd ?? 0,
            inputProfile: inputProfile);
    }

    private void ScheduleGameplayExitRestore(string? targetProcessName)
    {
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation;
        Task previousTask;

        lock (_gameplayExitRestoreSync)
        {
            if (_disposed)
            {
                cancellation.Dispose();
                return;
            }

            previousCancellation = _gameplayExitRestoreCancellation;
            previousTask = _gameplayExitRestoreTask;
            previousCancellation?.Cancel();

            _gameplayExitRestoreCancellation = cancellation;
            _gameplayExitRestoreTask = RestoreAfterGameplayExitAsync(
                NormalizeProcessToken(targetProcessName),
                cancellation.Token);
        }

        if (previousCancellation is not null)
        {
            _ = DisposeGameplayExitCancellationWhenSafeAsync(
                previousCancellation,
                previousTask);
        }
    }

    private void CancelGameplayExitRestore()
    {
        CancellationTokenSource? cancellation;
        Task task;
        lock (_gameplayExitRestoreSync)
        {
            cancellation = _gameplayExitRestoreCancellation;
            task = _gameplayExitRestoreTask;
            _gameplayExitRestoreCancellation = null;
            _gameplayExitRestoreTask = Task.CompletedTask;
            cancellation?.Cancel();
        }

        if (cancellation is not null)
        {
            _ = DisposeGameplayExitCancellationWhenSafeAsync(cancellation, task);
        }
    }

    private async Task RestoreAfterGameplayExitAsync(
        string targetProcessName,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(GameplayExitRestoreDelay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (ForegroundContext.Current?.Context?.HasSignal(InputContextSignalKind.Game) == true)
            {
                return;
            }

            if (IsAutomationEnabled)
            {
                _automation.RequestCurrentReapply(InputContextTrigger.GameplayExit);
                await _automation.WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(GameplayExitRefreshDelay, cancellationToken).ConfigureAwait(false);
            ForegroundContext.RequestCurrentRefresh(InputContextTrigger.GameplayExit);
            await ForegroundContext.WaitForIdleAsync(cancellationToken).ConfigureAwait(false);

            var restoreResult = IsAutomationEnabled
                ? "reapplied-and-refreshed"
                : "refreshed-automation-paused";
            lock (_gameplayExitRestoreSync)
            {
                _gameplayExitRestoreCount++;
                _gameplayExitRestoreLastAt = DateTimeOffset.UtcNow;
                _gameplayExitRestoreLastTarget = targetProcessName;
                _gameplayExitRestoreLastResult = restoreResult;
            }

            Trace.WriteLine(
                $"[FlowIME.GameplayKeyboard] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=gameplay-exit-restore target={targetProcessName} " +
                $"result={restoreResult}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            lock (_gameplayExitRestoreSync)
            {
                _gameplayExitRestoreLastAt = DateTimeOffset.UtcNow;
                _gameplayExitRestoreLastTarget = targetProcessName;
                _gameplayExitRestoreLastResult = $"failed-{ex.GetType().Name}";
            }
            Trace.WriteLine(
                $"[FlowIME.GameplayKeyboard] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=gameplay-exit-restore target={targetProcessName} " +
                $"result=failed type={ex.GetType().Name}");
        }
    }

    private static string NormalizeProcessToken(string? processName) =>
        string.IsNullOrWhiteSpace(processName)
            ? "unknown"
            : processName.Trim().Replace('|', '_').Replace('\r', '_').Replace('\n', '_');

    private static async Task DisposeGameplayExitCancellationWhenSafeAsync(
        CancellationTokenSource cancellation,
        Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // RestoreAfterGameplayExitAsync owns diagnostics for unexpected failures.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task RecoverAfterSystemTransitionAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SystemRecoveryDelay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Trace.WriteLine(
                $"[FlowIME.Lifecycle] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=system-recovery reason={reason} " +
                $"automationEnabled={IsAutomationEnabled}");

            ForegroundContext.RequestCurrentRefresh(InputContextTrigger.SystemRecovery);
            if (IsAutomationEnabled)
            {
                _automation.RequestCurrentReapply(InputContextTrigger.SystemRecovery);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced the delayed recovery request. The normal disposal path
            // owns teardown and no recovery work is required.
        }
        catch (Exception ex)
        {
            Trace.WriteLine(
                $"[FlowIME.Lifecycle] utc={DateTimeOffset.UtcNow:O} " +
                $"stage=system-recovery result=failed type={ex.GetType().Name}");
        }
    }

    private static async Task DisposeRecoveryCancellationWhenSafeAsync(
        CancellationTokenSource cancellation,
        Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // RecoverAfterSystemTransitionAsync already records unexpected failures.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static RollingFileTraceListener? TryCreateAutomationTraceListener(AppPaths paths)
    {
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            var listener = new RollingFileTraceListener(
                paths.AutomationLogPath,
                maxBytes: 2 * 1024 * 1024,
                archiveCount: 3)
            {
                Name = "FlowIME.AutomationFile"
            };

            Trace.Listeners.Add(listener);
            Trace.AutoFlush = false;
            Trace.WriteLine(
                $"[FlowIME.Automation] stage=session-start utc={DateTimeOffset.UtcNow:O}");
            return listener;
        }
        catch
        {
            // Diagnostics must never prevent FlowIME from starting. Native automation
            // remains functional even when the log directory is unavailable.
            return null;
        }
    }

    private static string FormatProviderCapabilities(
        FlowIME.Core.Models.InputMethodProviderCapabilities capabilities)
    {
        var values = new List<string>(6);
        if (capabilities.CanDetectActiveProfile) values.Add("detect");
        if (capabilities.CanActivateProfile) values.Add("activate");
        if (capabilities.CanReadMode) values.Add("read");
        if (capabilities.CanSetChinese) values.Add("set-chinese");
        if (capabilities.CanSetEnglish) values.Add("set-english");
        if (capabilities.RequiresPostActivationSettling) values.Add("settle");
        return string.Join(",", values);
    }

    private static string FormatContextSignals(InputContextSnapshot? context)
    {
        if (context is null)
        {
            return "none";
        }

        return FormatContextSignalKinds(
            context.Signals
                .Select(signal => signal.Kind)
                .ToArray());
    }

    private static string FormatContextSignalKinds(
        IReadOnlyList<InputContextSignalKind> signals)
    {
        if (signals.Count == 0)
        {
            return "none";
        }

        return string.Join(
            ",",
            signals
                .Select(signal => signal.ToString())
                .Distinct(StringComparer.Ordinal));
    }

    private static string FormatContextSignalDetails(InputContextSnapshot? context)
    {
        if (context is null || context.Signals.Count == 0)
        {
            return "none";
        }

        return string.Join(
            ";",
            context.Signals.Select(signal =>
                $"{signal.Kind}@{SanitizeDiagnosticToken(signal.Source)}:{signal.Confidence}"));
    }

    private static string SanitizeDiagnosticToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value
            .Replace('|', '_')
            .Replace(';', '_')
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Trim();
    }

    private static string FormatGameplayEvidence(
        IReadOnlyList<GameplayEvidenceKind> evidence)
    {
        if (evidence.Count == 0)
        {
            return "none";
        }

        return string.Join(
            ",",
            evidence.Select(item => item.ToString()).Distinct(StringComparer.Ordinal));
    }

    private static string FormatNullable(bool? value) =>
        value is null ? "unknown" : value.Value.ToString();

    private static string FormatKeyboardLayout(nint? keyboardLayout) =>
        keyboardLayout is null or 0
            ? "none"
            : $"0x{unchecked((uint)(nuint)keyboardLayout.Value):X8}";

    private static string FormatFileName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "none" : Path.GetFileName(path);

    private static string FormatLogFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "missing";
            }

            return $"{Path.GetFileName(path)}:{new FileInfo(path).Length}bytes";
        }
        catch
        {
            return $"{Path.GetFileName(path)}:unavailable";
        }
    }

    private static string SanitizeDiagnosticValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        var sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            sanitized = sanitized.Replace(
                userProfile,
                "%USERPROFILE%",
                StringComparison.OrdinalIgnoreCase);
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            sanitized = sanitized.Replace(
                localAppData,
                "%LOCALAPPDATA%",
                StringComparison.OrdinalIgnoreCase);
        }

        return sanitized;
    }
}
