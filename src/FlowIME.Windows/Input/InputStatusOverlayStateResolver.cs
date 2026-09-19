using FlowIME.Core.Models;

namespace FlowIME.Windows.Input;

public static class InputStatusOverlayStateResolver
{
    public static string? ResolveLabel(
        InputMode mode,
        nint keyboardLayout,
        bool gameplayUsBaselineActive)
    {
        if (gameplayUsBaselineActive && GameplayKeyboardBaseline.IsStandardUsKeyboard(keyboardLayout))
        {
            return "US";
        }

        return mode switch
        {
            InputMode.Chinese => "中",
            InputMode.English => "EN",
            _ when GameplayKeyboardBaseline.IsStandardUsKeyboard(keyboardLayout) => "US",
            _ => null
        };
    }
}
