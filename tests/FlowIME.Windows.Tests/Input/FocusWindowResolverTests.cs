using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class FocusWindowResolverTests
{
    [Fact]
    public void Resolve_prefers_hwndFocus_from_target_gui_thread()
    {
        var native = new FakeFocusWindowNativeApi
        {
            ThreadId = 123,
            GuiInfoSuccess = true,
            GuiInfo = new FocusGuiThreadInfo(
                ActiveWindow: (nint)0x1000,
                FocusWindow: (nint)0x2000,
                CaretWindow: (nint)0x3000,
                Flags: 0)
        };

        var result = new FocusWindowResolver(native).Resolve((nint)0x1000);

        Assert.True(result.GuiThreadInfoAvailable);
        Assert.Equal((uint)123, result.ThreadId);
        Assert.Equal((nint)0x2000, result.EffectiveInputWindow);
        Assert.Equal(FocusWindowSource.Focus, result.Source);
    }

    [Fact]
    public void Resolve_uses_caret_when_focus_is_unavailable()
    {
        var native = new FakeFocusWindowNativeApi
        {
            ThreadId = 123,
            GuiInfoSuccess = true,
            GuiInfo = new FocusGuiThreadInfo(
                ActiveWindow: (nint)0x1000,
                FocusWindow: 0,
                CaretWindow: (nint)0x3000,
                Flags: 0)
        };

        var result = new FocusWindowResolver(native).Resolve((nint)0x1000);

        Assert.Equal((nint)0x3000, result.EffectiveInputWindow);
        Assert.Equal(FocusWindowSource.Caret, result.Source);
    }

    [Fact]
    public void Resolve_falls_back_to_top_level_when_gui_thread_info_fails()
    {
        var native = new FakeFocusWindowNativeApi
        {
            ThreadId = 123,
            GuiInfoSuccess = false,
            NativeError = 5
        };

        var result = new FocusWindowResolver(native).Resolve((nint)0x1000);

        Assert.False(result.GuiThreadInfoAvailable);
        Assert.Equal((nint)0x1000, result.EffectiveInputWindow);
        Assert.Equal(FocusWindowSource.TopLevelFallback, result.Source);
        Assert.Equal(5, result.NativeError);
    }

    [Fact]
    public void Resolve_returns_error_when_window_thread_cannot_be_resolved()
    {
        var native = new FakeFocusWindowNativeApi { ThreadId = 0, NativeError = 1400 };

        var result = new FocusWindowResolver(native).Resolve((nint)0x1000);

        Assert.False(result.GuiThreadInfoAvailable);
        Assert.Equal((uint)0, result.ThreadId);
        Assert.Equal((nint)0x1000, result.EffectiveInputWindow);
        Assert.Equal(1400, result.NativeError);
    }

    private sealed class FakeFocusWindowNativeApi : IFocusWindowNativeApi
    {
        public uint ThreadId { get; init; }
        public bool GuiInfoSuccess { get; init; }
        public FocusGuiThreadInfo GuiInfo { get; init; }
        public int NativeError { get; init; }

        public uint GetWindowThreadId(nint hwnd, out int nativeError)
        {
            nativeError = ThreadId == 0 ? NativeError : 0;
            return ThreadId;
        }

        public bool TryGetGuiThreadInfo(
            uint threadId,
            out FocusGuiThreadInfo info,
            out int nativeError)
        {
            info = GuiInfo;
            nativeError = GuiInfoSuccess ? 0 : NativeError;
            return GuiInfoSuccess;
        }
    }
}
