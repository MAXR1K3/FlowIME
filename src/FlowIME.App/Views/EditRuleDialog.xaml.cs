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

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Keep the native action row fixed while expanded advanced settings scroll.
        if (GetTemplateChild("ContentScrollViewer") is ScrollViewer scroll)
        {
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.VerticalScrollMode = ScrollMode.Enabled;
        }
    }

    public EditRuleViewModel ViewModel { get; }
}
