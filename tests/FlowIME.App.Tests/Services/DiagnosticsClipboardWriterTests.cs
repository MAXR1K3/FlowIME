using System.Runtime.InteropServices;
using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class DiagnosticsClipboardWriterTests
{
    [Fact]
    public async Task Write_succeeds_without_retry_when_clipboard_accepts_content()
    {
        var writes = new List<string>();
        var delays = 0;
        var writer = new DiagnosticsClipboardWriter(
            writes.Add,
            (_, _) =>
            {
                delays++;
                return ValueTask.CompletedTask;
            });

        var result = await writer.WriteTextAsync(
            "diagnostics",
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(new[] { "diagnostics" }, writes);
        Assert.Equal(0, delays);
    }

    [Fact]
    public async Task Write_retries_transient_com_failures_and_preserves_hresult()
    {
        var attempts = 0;
        var delays = 0;
        var writer = new DiagnosticsClipboardWriter(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new COMException(
                        "clipboard busy",
                        unchecked((int)0x800401D0));
                }
            },
            (_, _) =>
            {
                delays++;
                return ValueTask.CompletedTask;
            });

        var result = await writer.WriteTextAsync(
            "diagnostics",
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, delays);
        Assert.Null(result.ErrorType);
        Assert.Null(result.HResult);
    }

    [Fact]
    public async Task Write_returns_diagnostic_failure_after_bounded_retries()
    {
        var writer = new DiagnosticsClipboardWriter(
            _ => throw new COMException(
                "clipboard busy",
                unchecked((int)0x800401D0)),
            (_, _) => ValueTask.CompletedTask);

        var result = await writer.WriteTextAsync(
            "diagnostics",
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(nameof(COMException), result.ErrorType);
        Assert.Equal(unchecked((int)0x800401D0), result.HResult);
        Assert.Equal("clipboard busy", result.Message);
    }
}
