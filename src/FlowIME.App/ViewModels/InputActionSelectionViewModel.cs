using CommunityToolkit.Mvvm.ComponentModel;
using FlowIME.Core.Models;

namespace FlowIME.App.ViewModels;

public abstract class InputActionSelectionViewModel : ObservableObject
{
    protected static IReadOnlyList<InputActionOption> GlobalDefaultActionOptions { get; } =
        Array.AsReadOnly(
        new[]
        {
            new InputActionOption("中文", InputAction.Chinese),
            new InputActionOption("英文", InputAction.English)
        });

    protected static IReadOnlyList<InputActionOption> ApplicationRuleActionOptions { get; } =
        Array.AsReadOnly(
        new[]
        {
            new InputActionOption("保持（不切换）", InputAction.Keep),
            new InputActionOption("中文", InputAction.Chinese),
            new InputActionOption("英文", InputAction.English)
        });

    private InputAction _selectedAction;

    protected InputActionSelectionViewModel(
        IReadOnlyList<InputActionOption> actionOptions,
        InputAction selectedAction)
    {
        ArgumentNullException.ThrowIfNull(actionOptions);
        if (actionOptions.Count == 0)
        {
            throw new ArgumentException("At least one input action option is required.", nameof(actionOptions));
        }

        if (actionOptions.Select(option => option.Action).Distinct().Count() != actionOptions.Count)
        {
            throw new ArgumentException("Input action options must not contain duplicate actions.", nameof(actionOptions));
        }

        ActionOptions = actionOptions;
        EnsureSupported(selectedAction);
        _selectedAction = selectedAction;
    }

    public IReadOnlyList<InputActionOption> ActionOptions { get; }

    public InputAction SelectedAction
    {
        get => _selectedAction;
        set
        {
            EnsureSupported(value);
            if (SetProperty(ref _selectedAction, value))
            {
                OnPropertyChanged(nameof(SelectedActionOption));
            }
        }
    }

    public InputActionOption SelectedActionOption
    {
        get => ActionOptions.Single(option => option.Action == SelectedAction);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SelectedAction = value.Action;
        }
    }

    private void EnsureSupported(InputAction action)
    {
        if (!ActionOptions.Any(option => option.Action == action))
        {
            throw new ArgumentOutOfRangeException(
                nameof(action),
                action,
                "The input action is not available for this editor.");
        }
    }
}

public sealed record InputActionOption(string Label, InputAction Action);
