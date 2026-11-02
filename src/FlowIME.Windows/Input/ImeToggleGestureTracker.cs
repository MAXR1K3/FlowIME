namespace FlowIME.Windows.Input;

public enum ImeToggleGesture
{
    None,
    LoneShift,
    CtrlSpace
}

/// <summary>
/// Recognizes the keyboard gestures that Chinese IMEs use to toggle between
/// Chinese and English: a lone Shift tap and Ctrl+Space. Only modifier/Space
/// transitions are inspected; typed characters are never recorded.
///
/// Recognition is a hint, not proof. The coordinator reads the resulting mode
/// afterwards, so a Shift tap that did not toggle anything just records the mode
/// that is already active.
/// </summary>
internal sealed class ImeToggleGestureTracker
{
    private uint _loneShiftCandidate;

    /// <param name="modifiers">Modifier state before this key transition.</param>
    internal ImeToggleGesture OnKey(
        uint virtualKey,
        bool isDown,
        GameplayModifierState modifiers)
    {
        if (IsShift(virtualKey))
        {
            if (isDown)
            {
                // Auto-repeat delivers repeated key-downs for the held Shift.
                if (_loneShiftCandidate != virtualKey)
                {
                    _loneShiftCandidate =
                        modifiers is { Ctrl: false, Alt: false, Win: false, Shift: false }
                            ? virtualKey
                            : 0;
                }

                return ImeToggleGesture.None;
            }

            var lone = _loneShiftCandidate == virtualKey;
            _loneShiftCandidate = 0;
            return lone ? ImeToggleGesture.LoneShift : ImeToggleGesture.None;
        }

        if (!isDown)
        {
            return ImeToggleGesture.None;
        }

        // Any other key pressed while Shift is held (Shift+A, Shift+Tab, Ctrl...)
        // means Shift was used as a modifier, not as a toggle.
        _loneShiftCandidate = 0;

        return virtualKey == GameplayHotkeyChordMatcher.VkSpace &&
               modifiers is { Ctrl: true, Alt: false, Win: false, Shift: false }
            ? ImeToggleGesture.CtrlSpace
            : ImeToggleGesture.None;
    }

    private static bool IsShift(uint virtualKey) =>
        virtualKey is GameplayHotkeyChordMatcher.VkShift or
            GameplayHotkeyChordMatcher.VkLeftShift or
            GameplayHotkeyChordMatcher.VkRightShift;
}
