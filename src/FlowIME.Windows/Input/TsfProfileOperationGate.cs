using System.Diagnostics;

namespace FlowIME.Windows.Input;

/// <summary>
/// Serializes access to the process-wide TSF profile manager. The COM class can
/// return the same MTA RCW identity to concurrent callers, so forced release and
/// overlapping profile operations must not race.
/// </summary>
internal static class TsfProfileOperationGate
{
    private static readonly object Sync = new();

    public static T Run<T>(string operation, Func<T> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);

        lock (Sync)
        {
            Trace.WriteLine(
                $"[FlowIME.TSF] utc={DateTimeOffset.UtcNow:O} operation={operation} " +
                $"stage=entered thread={Environment.CurrentManagedThreadId}");
            try
            {
                var result = action();
                Trace.WriteLine(
                    $"[FlowIME.TSF] utc={DateTimeOffset.UtcNow:O} operation={operation} " +
                    $"stage=completed thread={Environment.CurrentManagedThreadId}");
                return result;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(
                    $"[FlowIME.TSF] utc={DateTimeOffset.UtcNow:O} operation={operation} " +
                    $"stage=exception thread={Environment.CurrentManagedThreadId} " +
                    $"type={ex.GetType().Name} hresult=0x{unchecked((uint)ex.HResult):X8} " +
                    $"message={Sanitize(ex.Message)}");
                throw;
            }
        }
    }

    private static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "none"
            : value.Replace('\r', ' ').Replace('\n', ' ').Replace(' ', '_');
}
