namespace FlowIME.Windows.Input;

public sealed class LegacyImeOpenStatusAccessor : IInputStateAccessor
{
    private const nuint ImcGetOpenStatus = 0x0005;
    private const nuint ImcSetOpenStatus = 0x0006;

    private readonly IImeNativeApi _native;

    public LegacyImeOpenStatusAccessor()
        : this(new Win32ImeNativeApi())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("IMM open-status access requires Windows.");
        }
    }

    internal LegacyImeOpenStatusAccessor(IImeNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public ImeOpenStatusResult GetOpenStatus(nint targetWindow)
    {
        if (targetWindow == 0)
        {
            return new ImeOpenStatusResult(
                false,
                false,
                0,
                0,
                "Target HWND is zero.");
        }

        var imeWindow = _native.GetDefaultImeWindow(targetWindow);
        if (imeWindow == 0)
        {
            return new ImeOpenStatusResult(
                false,
                false,
                0,
                0,
                "ImmGetDefaultIMEWnd returned NULL.");
        }

        if (!_native.TrySendImeControl(
                imeWindow,
                ImcGetOpenStatus,
                0,
                out var result,
                out var nativeError))
        {
            return new ImeOpenStatusResult(
                false,
                false,
                imeWindow,
                nativeError,
                "WM_IME_CONTROL/IMC_GETOPENSTATUS failed or timed out.");
        }

        return new ImeOpenStatusResult(
            true,
            result != 0,
            imeWindow,
            0,
            null);
    }

    public ImeSetOpenStatusResult SetOpenStatus(nint targetWindow, bool open)
    {
        if (targetWindow == 0)
        {
            return new ImeSetOpenStatusResult(
                false,
                open,
                0,
                0,
                "Target HWND is zero.");
        }

        var imeWindow = _native.GetDefaultImeWindow(targetWindow);
        if (imeWindow == 0)
        {
            return new ImeSetOpenStatusResult(
                false,
                open,
                0,
                0,
                "ImmGetDefaultIMEWnd returned NULL.");
        }

        if (!_native.TrySendImeControl(
                imeWindow,
                ImcSetOpenStatus,
                (nint)(open ? 1 : 0),
                out _,
                out var nativeError))
        {
            return new ImeSetOpenStatusResult(
                false,
                open,
                imeWindow,
                nativeError,
                "WM_IME_CONTROL/IMC_SETOPENSTATUS failed or timed out.");
        }

        return new ImeSetOpenStatusResult(
            true,
            open,
            imeWindow,
            0,
            null);
    }
}
