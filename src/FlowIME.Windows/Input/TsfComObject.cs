using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlowIME.Windows.Input;

internal static class TsfComObject
{
    public static void Release(ref object? instance, string operation)
    {
        var current = Interlocked.Exchange(ref instance, null);
        if (current is null || !Marshal.IsComObject(current))
        {
            return;
        }

        try
        {
            _ = Marshal.ReleaseComObject(current);
        }
        catch (InvalidComObjectException ex)
        {
            Trace.WriteLine(
                $"[FlowIME.TSF] stage=release result=already-released " +
                $"operation={operation} thread={Environment.CurrentManagedThreadId} " +
                $"hresult=0x{unchecked((uint)ex.HResult):X8}");
        }
    }
}
