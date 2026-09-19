namespace FlowIME.Windows.Input;

public interface IInputStateAccessor
{
    ImeOpenStatusResult GetOpenStatus(nint targetWindow);

    ImeSetOpenStatusResult SetOpenStatus(nint targetWindow, bool open);
}
