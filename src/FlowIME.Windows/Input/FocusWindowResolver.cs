namespace FlowIME.Windows.Input;

/// <summary>
/// Resolves the HWND that currently owns keyboard input inside a top-level window.
/// For an active GUI thread, hwndFocus is the primary target used by established
/// IMM helpers before calling ImmGetDefaultIMEWnd.
/// </summary>
public sealed class FocusWindowResolver : IFocusWindowResolver
{
    private readonly IFocusWindowNativeApi _native;

    public FocusWindowResolver()
        : this(new Win32FocusWindowNativeApi())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("GUI thread focus resolution requires Windows.");
        }
    }

    internal FocusWindowResolver(IFocusWindowNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public FocusWindowResult Resolve(nint topLevelWindow)
    {
        if (topLevelWindow == 0)
        {
            return new FocusWindowResult(
                0, 0, 0, 0, 0, 0,
                FocusWindowSource.TopLevelFallback,
                false,
                0,
                "Top-level HWND is zero.");
        }

        var threadId = _native.GetWindowThreadId(topLevelWindow, out var threadError);
        if (threadId == 0)
        {
            return new FocusWindowResult(
                topLevelWindow, 0, 0, 0, 0, topLevelWindow,
                FocusWindowSource.TopLevelFallback,
                false,
                threadError,
                "GetWindowThreadProcessId failed for the target window.");
        }

        if (!_native.TryGetGuiThreadInfo(threadId, out var info, out var guiError))
        {
            return new FocusWindowResult(
                topLevelWindow, threadId, 0, 0, 0, topLevelWindow,
                FocusWindowSource.TopLevelFallback,
                false,
                guiError,
                "GetGUIThreadInfo failed for the target GUI thread.");
        }

        if (info.FocusWindow != 0)
        {
            return new FocusWindowResult(
                topLevelWindow, threadId,
                info.ActiveWindow, info.FocusWindow, info.CaretWindow,
                info.FocusWindow,
                FocusWindowSource.Focus,
                true,
                0,
                null);
        }

        if (info.CaretWindow != 0)
        {
            return new FocusWindowResult(
                topLevelWindow, threadId,
                info.ActiveWindow, info.FocusWindow, info.CaretWindow,
                info.CaretWindow,
                FocusWindowSource.Caret,
                true,
                0,
                null);
        }

        return new FocusWindowResult(
            topLevelWindow, threadId,
            info.ActiveWindow, info.FocusWindow, info.CaretWindow,
            topLevelWindow,
            FocusWindowSource.TopLevelFallback,
            true,
            0,
            "GUI thread info did not expose hwndFocus or hwndCaret.");
    }
}
