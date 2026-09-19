using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

internal sealed class ComInitialization : IDisposable
{
    private readonly bool _mustUninitialize;

    private ComInitialization(bool mustUninitialize)
    {
        _mustUninitialize = mustUninitialize;
    }

    internal static ComInitialization EnterMta()
    {
        var hr = TsfInterop.CoInitializeEx(0, TsfInterop.CoinitMultithreaded);
        if (hr < 0 && hr != TsfInterop.RpcEChangedMode)
        {
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
        }

        return new ComInitialization(hr is 0 or 1);
    }

    public void Dispose()
    {
        if (_mustUninitialize)
        {
            TsfInterop.CoUninitialize();
        }
    }
}
