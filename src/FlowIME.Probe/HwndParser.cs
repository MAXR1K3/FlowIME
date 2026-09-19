using System.Globalization;

namespace FlowIME.Probe;

internal static class HwndParser
{
    internal static bool TryParse(string value, out nint hwnd)
    {
        hwnd = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();
        var style = NumberStyles.Integer;
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
            style = NumberStyles.AllowHexSpecifier;
        }

        if (!long.TryParse(span, style, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            return false;
        }

        hwnd = (nint)parsed;
        return true;
    }
}
