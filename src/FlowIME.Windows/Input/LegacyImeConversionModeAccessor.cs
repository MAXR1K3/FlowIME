namespace FlowIME.Windows.Input;

/// <summary>
/// Experimental cross-process IMM conversion-mode probe. This intentionally uses
/// WM_IME_CONTROL against the target window's default IME window so we can test
/// whether Microsoft Pinyin still exposes effective Chinese/English state through
/// the legacy conversion-mode bridge on current Windows 11 builds.
/// </summary>
public sealed class LegacyImeConversionModeAccessor : IImeConversionModeAccessor
{
    private const nuint ImcGetConversionMode = 0x0001;
    private const nuint ImcSetConversionMode = 0x0002;

    public const uint ImeCmodeNative = 0x0001;

    private readonly IImeNativeApi _native;

    public LegacyImeConversionModeAccessor()
        : this(new Win32ImeNativeApi())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("IMM conversion-mode access requires Windows.");
        }
    }

    internal LegacyImeConversionModeAccessor(IImeNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public ImeConversionModeResult GetConversionMode(nint targetWindow)
    {
        if (targetWindow == 0)
        {
            return new ImeConversionModeResult(false, 0, 0, 0, "Target HWND is zero.");
        }

        var imeWindow = _native.GetDefaultImeWindow(targetWindow);
        if (imeWindow == 0)
        {
            return new ImeConversionModeResult(
                false,
                0,
                0,
                0,
                "ImmGetDefaultIMEWnd returned NULL.");
        }

        if (!_native.TrySendImeControl(
                imeWindow,
                ImcGetConversionMode,
                0,
                out var result,
                out var nativeError))
        {
            return new ImeConversionModeResult(
                false,
                0,
                imeWindow,
                nativeError,
                "WM_IME_CONTROL/IMC_GETCONVERSIONMODE failed or timed out.");
        }

        return new ImeConversionModeResult(
            true,
            checked((uint)result),
            imeWindow,
            0,
            null);
    }

    public ImeSetConversionModeResult SetConversionMode(nint targetWindow, uint conversionMode)
    {
        if (targetWindow == 0)
        {
            return new ImeSetConversionModeResult(
                false,
                conversionMode,
                0,
                0,
                "Target HWND is zero.");
        }

        var imeWindow = _native.GetDefaultImeWindow(targetWindow);
        if (imeWindow == 0)
        {
            return new ImeSetConversionModeResult(
                false,
                conversionMode,
                0,
                0,
                "ImmGetDefaultIMEWnd returned NULL.");
        }

        if (!_native.TrySendImeControl(
                imeWindow,
                ImcSetConversionMode,
                (nint)conversionMode,
                out _,
                out var nativeError))
        {
            return new ImeSetConversionModeResult(
                false,
                conversionMode,
                imeWindow,
                nativeError,
                "WM_IME_CONTROL/IMC_SETCONVERSIONMODE failed or timed out.");
        }

        return new ImeSetConversionModeResult(
            true,
            conversionMode,
            imeWindow,
            0,
            null);
    }
}
