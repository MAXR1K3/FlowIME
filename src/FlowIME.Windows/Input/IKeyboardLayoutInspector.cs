namespace FlowIME.Windows.Input;

public interface IKeyboardLayoutInspector
{
    nint GetKeyboardLayout(uint threadId);
}
