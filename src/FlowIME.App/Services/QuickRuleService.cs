using FlowIME.App.ViewModels;
using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Core.Rules;

namespace FlowIME.App.Services;

internal sealed record QuickRuleResult(
    string Title,
    string Message,
    ApplicationRule? Rule);

/// <summary>
/// Applies a tray quick-rule choice with the same rule semantics as the "add
/// application" dialog: an existing rule for the application is updated in
/// place (keeping its priority, provider and extra match conditions), otherwise
/// a new executable-scoped rule is appended at the lowest priority, using the
/// global default's input method.
/// </summary>
internal sealed class QuickRuleService(
    IRuleRepository repository,
    IReadOnlyList<InputMethodProviderDescriptor> providers,
    Func<string> defaultProviderId)
{
    private readonly IRuleRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IReadOnlyList<InputMethodProviderDescriptor> _providers =
        providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly Func<string> _defaultProviderId =
        defaultProviderId ?? throw new ArgumentNullException(nameof(defaultProviderId));

    public async ValueTask<QuickRuleResult> ApplyAsync(
        QuickRuleTarget target,
        QuickRuleChoice choice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var rules = new RulesViewModel(_repository, _providers);
        await rules.LoadAsync(cancellationToken);
        var application = ToApplication(target);
        var existing =
            (target.ExistingRule is { } known
                ? rules.SourceRules.FirstOrDefault(rule => rule.Id == known.Id)
                : null) ??
            rules.FindByApplication(application);

        if (choice == QuickRuleChoice.Remove)
        {
            if (existing is not null)
            {
                await rules.DeleteRuleAsync(existing.Id, cancellationToken);
            }

            return new QuickRuleResult(
                $"已移除{target.DisplayName}的规则",
                "这个应用将改用全局默认场景。",
                null);
        }

        var action = choice switch
        {
            QuickRuleChoice.Chinese => InputAction.Chinese,
            QuickRuleChoice.English => InputAction.English,
            _ => InputAction.Keep
        };

        ApplicationRule saved;
        if (existing is not null)
        {
            saved = existing with
            {
                Enabled = true,
                Action = action,
                // A US-keyboard rule carries no provider; fall back to the default.
                ProviderId = existing.ProviderId is { } providerId
                    ? InputMethodProviderIds.Normalize(providerId)
                    : ResolveDefaultProviderId(rules)
            };
            await rules.UpdateRuleAsync(saved, cancellationToken);
        }
        else
        {
            saved = new ApplicationRule(
                Guid.NewGuid(),
                true,
                rules.GetNextPriority(),
                new ApplicationMatch(
                    ProcessPath: application.ExecutablePath,
                    PackageFamilyName: application.PackageFamilyName,
                    ApplicationUserModelId: application.ApplicationUserModelId),
                action,
                target.DisplayName,
                ResolveDefaultProviderId(rules));
            await rules.UpsertRuleAsync(saved, cancellationToken);
        }

        return new QuickRuleResult(
            $"{target.DisplayName} · {DescribeAction(action)}",
            "回到这个应用时生效。",
            saved);
    }

    /// <summary>
    /// The tray has no provider picker, so follow the input method the user's
    /// global default already uses; the registry default is only a fallback.
    /// </summary>
    private string ResolveDefaultProviderId(RulesViewModel rules) =>
        InputMethodProviderIds.Normalize(rules.GlobalDefault?.ProviderId ?? _defaultProviderId());

    private static string DescribeAction(InputAction action) => action switch
    {
        InputAction.Chinese => "进入时切换到中文",
        InputAction.English => "进入时切换到英文",
        _ => "进入时保持不变"
    };

    private static RunningApplication ToApplication(QuickRuleTarget target) =>
        new(
            target.DisplayName,
            target.Window.ProcessName,
            target.Window.ExecutablePath!,
            target.Window.Hwnd,
            target.Window.ProcessId,
            target.Window.PackageFamilyName,
            target.Window.ApplicationUserModelId);
}
