using FlowIME.Probe.Commands;

namespace FlowIME.Probe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("FlowIME.Probe must run on Windows.");
            return ProbeExitCode.BackendFailure;
        }

        if (args.Length == 0)
        {
            PrintUsage();
            return ProbeExitCode.InvalidArguments;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var command = args[0].ToLowerInvariant();
            var commandArgs = args.Skip(1).ToArray();
            return command switch
            {
                "watch" => await WatchCommand.RunAsync(cancellation.Token),
                "inspect" => await InspectCommand.RunAsync(commandArgs, cancellation.Token),
                "set" => await SetModeCommand.RunAsync(commandArgs, cancellation.Token),
                "set-conversion" => await SetConversionModeCommand.RunAsync(commandArgs, cancellation.Token),
                "set-open" => await SetOpenStatusCommand.RunAsync(commandArgs, cancellation.Token),
                "capture" => await CaptureCommand.RunAsync(commandArgs, cancellation.Token),
                "compare-captures" => await CompareCapturesCommand.RunAsync(commandArgs, cancellation.Token),
                _ => UnknownCommand(command)
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return ProbeExitCode.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return ProbeExitCode.BackendFailure;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return ProbeExitCode.InvalidArguments;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FlowIME.Probe - Windows 11 IME feasibility probe");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  FlowIME.Probe watch");
        Console.WriteLine("  FlowIME.Probe inspect --foreground [--delay-ms 3000]");
        Console.WriteLine("  FlowIME.Probe inspect --hwnd 0x123456");
        Console.WriteLine("  FlowIME.Probe set --foreground --mode chinese|english");
        Console.WriteLine("  FlowIME.Probe set --hwnd 0x123456 --mode chinese|english");
        Console.WriteLine("  FlowIME.Probe set-conversion --foreground --mode chinese|english");
        Console.WriteLine("  FlowIME.Probe set-conversion --hwnd 0x123456 --mode chinese|english");
        Console.WriteLine("  FlowIME.Probe set-open --foreground --mode chinese|english [--delay-ms 3000]");
        Console.WriteLine("  FlowIME.Probe set-open --hwnd 0x123456 --mode chinese|english");
        Console.WriteLine("  FlowIME.Probe capture --foreground --label wechat-chinese --delay-ms 3000");
        Console.WriteLine("  FlowIME.Probe capture --foreground --label wechat-english --delay-ms 3000");
        Console.WriteLine("  FlowIME.Probe compare-captures --left <capture.json> --right <capture.json>");
    }
}
