using FlowIME.Core.Models;
using FlowIME.Windows.Windowing;

namespace FlowIME.Probe.Commands;

internal static class WatchCommand
{
    internal static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var foreground = new ForegroundWindowSource();
        var resolver = new WindowResolver();
        using var outputLock = new SemaphoreSlim(1, 1);

        async Task PrintAsync(nint hwnd)
        {
            try
            {
                var window = await resolver.ResolveAsync(hwnd, cancellationToken);
                if (window is null)
                {
                    return;
                }

                await outputLock.WaitAsync(cancellationToken);
                try
                {
                    Console.WriteLine();
                    Console.WriteLine($"[{DateTimeOffset.Now:O}] FOREGROUND");
                    ProbePrinter.PrintWindow(window);
                }
                finally
                {
                    outputLock.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"watch: {exception.Message}");
            }
        }

        void OnForegroundChanged(object? sender, ForegroundWindowChangedEventArgs e) =>
            _ = PrintAsync(e.Hwnd);

        foreground.ForegroundWindowChanged += OnForegroundChanged;
        try
        {
            Console.WriteLine("Watching EVENT_SYSTEM_FOREGROUND. Press Ctrl+C to stop.");
            var current = foreground.GetCurrentForegroundWindow();
            if (current != 0)
            {
                await PrintAsync(current);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreground.ForegroundWindowChanged -= OnForegroundChanged;
        }

        return ProbeExitCode.Success;
    }
}
