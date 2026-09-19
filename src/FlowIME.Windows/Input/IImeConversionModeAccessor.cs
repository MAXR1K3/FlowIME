namespace FlowIME.Windows.Input;

public interface IImeConversionModeAccessor
{
    ImeConversionModeResult GetConversionMode(nint targetWindow);

    ImeSetConversionModeResult SetConversionMode(
        nint targetWindow,
        uint conversionMode);
}
