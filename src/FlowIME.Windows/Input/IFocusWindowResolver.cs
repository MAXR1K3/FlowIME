namespace FlowIME.Windows.Input;

public interface IFocusWindowResolver
{
    FocusWindowResult Resolve(nint topLevelWindow);
}
