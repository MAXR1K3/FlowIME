using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

public enum TextEntryPresence
{
    Unknown,
    Present,
    Absent
}

/// <summary>
/// Reports whether keyboard focus in a window is on something the user can type
/// into. Only <see cref="TextEntryPresence.Absent"/> changes automation behavior,
/// so implementations must return <see cref="TextEntryPresence.Unknown"/> unless
/// the focused element is positively identified as a non-text control.
/// </summary>
public interface ITextEntryProbe
{
    TextEntryPresence Probe(WindowContext window);
}

/// <summary>
/// Raised when a caret becomes visible in the foreground process: a cheap hint
/// that the user has just entered a text box, including self-drawn editors that
/// never move native focus.
/// </summary>
public interface ITextEntrySignalSource
{
    event EventHandler<InputFocusChangedEventArgs>? CaretShown;
}
