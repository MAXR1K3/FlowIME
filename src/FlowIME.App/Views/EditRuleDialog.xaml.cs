using FlowIME.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FlowIME.App.Views;

public sealed partial class EditRuleDialog : ContentDialog
{
    public EditRuleDialog(EditRuleViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
    }

    public EditRuleViewModel ViewModel { get; }
}
