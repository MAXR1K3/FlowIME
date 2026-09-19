using FlowIME.App.Services;
using FlowIME.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FlowIME.App.Views;

public sealed partial class AddApplicationDialog : ContentDialog
{
    private readonly ApplicationIconLoader _icons = new();

    public AddApplicationDialog(AddApplicationViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Preserve the native dialog's fixed action row and focus handling.
        if (GetTemplateChild("ContentScrollViewer") is ScrollViewer scroll)
        {
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.VerticalScrollMode = ScrollMode.Enabled;
        }
    }

    public AddApplicationViewModel ViewModel { get; }

    private async void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        foreach (var item in ViewModel.ApplicationItems.ToArray())
        {
            item.Icon = await _icons.LoadAsync(item.ExecutablePath);
        }
    }

    private async void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        BrowseErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var window = ((App)Application.Current).MainWindow ??
                throw new InvalidOperationException("The main window is unavailable.");
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".exe");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var application = ExecutableCandidateFactory.Create(file.Path);
            var item = ViewModel.AddOrSelectApplication(application);
            item.Icon = await _icons.LoadAsync(item.ExecutablePath);
            ApplicationsList.ScrollIntoView(item);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            BrowseErrorText.Text = $"无法使用该可执行文件：{ex.Message}";
            BrowseErrorText.Visibility = Visibility.Visible;
        }
    }
}
