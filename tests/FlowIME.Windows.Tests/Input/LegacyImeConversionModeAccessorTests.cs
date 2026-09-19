using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class LegacyImeConversionModeAccessorTests
{
    [Fact]
    public void Get_reads_conversion_mode_and_native_bit()
    {
        var native = new FakeImeNativeApi
        {
            ImeWindow = (nint)0x2222,
            SendSuccess = true,
            SendResult = 0x0009
        };
        var accessor = new LegacyImeConversionModeAccessor(native);

        var result = accessor.GetConversionMode((nint)0x1234);

        Assert.True(result.Success);
        Assert.Equal((uint)0x0009, result.ConversionMode);
        Assert.True(result.IsNative);
        Assert.Equal((nuint)0x0001, native.LastCommand);
    }

    [Fact]
    public void Get_maps_zero_to_alphanumeric_candidate()
    {
        var native = new FakeImeNativeApi
        {
            ImeWindow = (nint)0x2222,
            SendSuccess = true,
            SendResult = 0
        };
        var accessor = new LegacyImeConversionModeAccessor(native);

        var result = accessor.GetConversionMode((nint)0x1234);

        Assert.True(result.Success);
        Assert.False(result.IsNative);
    }

    [Fact]
    public void Set_sends_requested_conversion_mode()
    {
        var native = new FakeImeNativeApi
        {
            ImeWindow = (nint)0x2222,
            SendSuccess = true
        };
        var accessor = new LegacyImeConversionModeAccessor(native);

        var result = accessor.SetConversionMode((nint)0x1234, 0x0009);

        Assert.True(result.Success);
        Assert.Equal((nuint)0x0002, native.LastCommand);
        Assert.Equal((nint)0x0009, native.LastParameter);
    }

    private sealed class FakeImeNativeApi : IImeNativeApi
    {
        public nint ImeWindow { get; init; }
        public bool SendSuccess { get; init; }
        public nuint SendResult { get; init; }
        public int NativeError { get; init; }
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
            LastCommand = command;
            LastParameter = parameter;
            result = SendResult;
            nativeError = NativeError;
            return SendSuccess;
        }
    }
}
