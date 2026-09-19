using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class LegacyImeOpenStatusAccessorTests
{
    [Fact]
    public void Get_returns_failure_when_default_ime_window_is_missing()
    {
        var native = new FakeImeNativeApi { ImeWindow = 0 };
        var accessor = new LegacyImeOpenStatusAccessor(native);

        var result = accessor.GetOpenStatus((nint)0x1234);

        Assert.False(result.Success);
        Assert.Contains("ImmGetDefaultIMEWnd", result.Error ?? string.Empty);
        Assert.Equal(0, native.SendCount);
    }

    [Fact]
    public void Get_maps_nonzero_native_result_to_open()
    {
        var native = new FakeImeNativeApi
        {
            ImeWindow = (nint)0x2222,
            SendSuccess = true,
            SendResult = 1
        };
        var accessor = new LegacyImeOpenStatusAccessor(native);

        var result = accessor.GetOpenStatus((nint)0x1234);

        Assert.True(result.Success);
        Assert.True(result.IsOpen);
        Assert.Equal((nint)0x2222, result.ImeWindow);
    }

    [Fact]
    public void Get_preserves_timeout_error()
    {
        var native = new FakeImeNativeApi
        {
            ImeWindow = (nint)0x2222,
            SendSuccess = false,
            NativeError = 1460
        };
        var accessor = new LegacyImeOpenStatusAccessor(native);

        var result = accessor.GetOpenStatus((nint)0x1234);

        Assert.False(result.Success);
        Assert.Equal(1460, result.NativeError);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Set_sends_requested_open_state(bool open, long expectedParameter)
    {
        var native = new FakeImeNativeApi
        {
            ImeWindow = (nint)0x2222,
            SendSuccess = true
        };
        var accessor = new LegacyImeOpenStatusAccessor(native);

        var result = accessor.SetOpenStatus((nint)0x1234, open);

        Assert.True(result.Success);
        Assert.Equal((nint)expectedParameter, native.LastParameter);
        Assert.Equal((nuint)0x0006, native.LastCommand);
    }

    private sealed class FakeImeNativeApi : IImeNativeApi
    {
        public nint ImeWindow { get; init; }
        public bool SendSuccess { get; init; }
        public nuint SendResult { get; init; }
        public int NativeError { get; init; }
        public int SendCount { get; private set; }
        public nuint LastCommand { get; private set; }
        public nint LastParameter { get; private set; }

        public nint GetDefaultImeWindow(nint targetWindow) => ImeWindow;

        public bool TrySendImeControl(
            nint imeWindow,
            nuint command,
            nint parameter,
            out nuint result,
            out int nativeError)
        {
            SendCount++;
            LastCommand = command;
            LastParameter = parameter;
            result = SendResult;
            nativeError = NativeError;
            return SendSuccess;
        }
    }
}
