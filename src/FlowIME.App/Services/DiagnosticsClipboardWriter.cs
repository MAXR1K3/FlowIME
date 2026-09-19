using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace FlowIME.App.Services;

internal sealed record DiagnosticsClipboardWriteResult(
    bool Success,
    int Attempts,
    string? ErrorType = null,
    int? HResult = null,
    string? Message = null);

internal sealed class DiagnosticsClipboardWriter
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(40);

    private readonly Action<string> _writeText;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delay;

    public DiagnosticsClipboardWriter()
        : this(WriteUsingWinRt, DelayAsync)
    {
    }

    internal DiagnosticsClipboardWriter(
        Action<string> writeText,
        Func<TimeSpan, CancellationToken, ValueTask> delay)
    {
        _writeText = writeText ?? throw new ArgumentNullException(nameof(writeText));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async ValueTask<DiagnosticsClipboardWriteResult> WriteTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _writeText(text);
                return new DiagnosticsClipboardWriteResult(true, attempt);
            }
            catch (COMException ex)
            {
                lastError = ex;
            }
            catch (Exception ex)
            {
                return Failure(attempt, ex);
            }

            if (attempt < MaxAttempts)
            {
                await _delay(RetryDelay, cancellationToken);
            }
        }

        return Failure(MaxAttempts, lastError!);
    }

    private static void WriteUsingWinRt(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    private static async ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
    }

    private static DiagnosticsClipboardWriteResult Failure(
        int attempts,
        Exception error) =>
        new(
            Success: false,
            Attempts: attempts,
            ErrorType: error.GetType().Name,
            HResult: error.HResult,
            Message: error.Message);
}
