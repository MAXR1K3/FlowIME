using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowIME.App.Views;

internal static class ContentDialogTheme
{
    public static T AttachToHost<T>(this T dialog, FrameworkElement host)
        where T : ContentDialog
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(host);

        dialog.XamlRoot = host.XamlRoot ??
            throw new InvalidOperationException(
                "A content dialog cannot be shown before its host enters the XAML tree.");
        dialog.RequestedTheme = host.ActualTheme;
        return dialog;
    }
}
