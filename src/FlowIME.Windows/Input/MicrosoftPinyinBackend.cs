using FlowIME.Core.Abstractions;
using FlowIME.Core.Automation;
using FlowIME.Core.InputMethods;
using FlowIME.Core.Models;

namespace FlowIME.Windows.Input;

/// <summary>
/// Backward-compatible Microsoft Pinyin backend facade.
///
/// Production composition now uses ProviderInputMethodBackend with an
/// InputMethodProviderRegistry. This facade remains so existing probes/tests and
/// any callers compiled against the P1-P4 type keep the same behavior while the
/// provider boundary is introduced without a migration flag day.
/// </summary>
public sealed class MicrosoftPinyinBackend : IInputMethodBackend
{
    public const uint ChineseConversionMode = MicrosoftPinyinProvider.ChineseConversionMode;
    public const uint EnglishConversionMode = MicrosoftPinyinProvider.EnglishConversionMode;

    private readonly MicrosoftPinyinProvider _provider;
    private readonly ProviderInputMethodBackend _inner;

    public MicrosoftPinyinBackend()
        : this(new MicrosoftPinyinProvider())
    {
    }

    internal MicrosoftPinyinBackend(
        IFocusWindowResolver focusResolver,
        IImeConversionModeAccessor conversionMode,
        IKeyboardLayoutInspector keyboardLayout,
        IMicrosoftPinyinProfileActivator profileActivator,
        IAutomationDelay? delay = null,
        IInputProfileInspector? profileInspector = null)
        : this(new MicrosoftPinyinProvider(
            focusResolver,
            conversionMode,
            keyboardLayout,
            profileActivator,
            delay,
            profileInspector))
    {
    }

    private MicrosoftPinyinBackend(MicrosoftPinyinProvider provider)
    {
        _provider = provider;
        var registry = new InputMethodProviderRegistry(
            [provider],
            MicrosoftPinyinProvider.ProviderId);
        _inner = new ProviderInputMethodBackend(registry);
    }

    public ValueTask<InputState> GetStateAsync(
        WindowContext window,
        CancellationToken cancellationToken = default) =>
        // This type is a compatibility facade for the historical Microsoft-Pinyin-only
        // backend contract. Do not run multi-provider active-profile detection here: callers
        // and the P1-P4 regression suite expect this facade to read Microsoft Pinyin
        // semantics directly. Production multi-provider composition uses
        // ProviderInputMethodBackend from AppServices instead.
        _provider.GetStateAsync(window, cancellationToken);

    public ValueTask<InputOperationResult> ApplyAsync(
        WindowContext window,
        InputAction action,
        CancellationToken cancellationToken = default) =>
        _inner.ApplyAsync(window, action, cancellationToken);
}
