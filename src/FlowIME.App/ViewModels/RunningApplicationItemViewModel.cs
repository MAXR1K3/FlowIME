using CommunityToolkit.Mvvm.ComponentModel;
using FlowIME.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FlowIME.App.ViewModels;

public sealed class RunningApplicationItemViewModel : ObservableObject
{
    private ImageSource? _icon;

    public RunningApplicationItemViewModel(RunningApplication application)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public RunningApplication Application { get; }

    public string DisplayName => Application.DisplayName;

    public string ProcessName => Application.ProcessName;

    public string ExecutablePath => Application.ExecutablePath;

    public string SourceLabel => Application.MainWindowHandle != 0
        ? "正在运行"
        : Application.ProcessId != 0
            ? "后台进程"
            : "手动选择";

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (!SetProperty(ref _icon, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IconVisibility));
            OnPropertyChanged(nameof(FallbackIconVisibility));
        }
    }

    public Visibility IconVisibility => Icon is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FallbackIconVisibility => Icon is null
        ? Visibility.Visible
        : Visibility.Collapsed;
}
