using FlowIME.Core.Models;

namespace FlowIME.Core.Abstractions;

public interface IInputFocusSource
{
    event EventHandler<InputFocusChangedEventArgs>? InputFocusChanged;
}
