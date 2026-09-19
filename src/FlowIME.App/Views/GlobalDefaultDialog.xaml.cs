using FlowIME.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FlowIME.App.Views;

public sealed partial class GlobalDefaultDialog : ContentDialog
{
    public GlobalDefaultDialog(GlobalDefaultViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
    }

    public GlobalDefaultViewModel ViewModel { get; }
}
