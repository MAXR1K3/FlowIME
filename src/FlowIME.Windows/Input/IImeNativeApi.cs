namespace FlowIME.Windows.Input;

internal interface IImeNativeApi
{
    nint GetDefaultImeWindow(nint targetWindow);

    bool TrySendImeControl(
        nint imeWindow,
        nuint command,
        nint parameter,
        out nuint result,
        out int nativeError);
}
