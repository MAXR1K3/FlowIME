using FlowIME.Windows.Input;
using FlowIME.Windows.Windowing;

namespace FlowIME.Probe.Commands;

internal static class SetModeCommand
{
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        using var foreground = new ForegroundWindowSource();
        if (!ProbeTargetParser.TryResolve(args, foreground, out var hwnd, out var error))
        {
            Console.Error.WriteLine(error);
            return ProbeExitCode.InvalidArguments;
        }

        if (!TryReadMode(args, out var mode))
        {
            Console.Error.WriteLine("--mode must be either chinese or english.");
            return ProbeExitCode.InvalidArguments;
        }

        var resolver = new WindowResolver();
        var window = await resolver.ResolveAsync(hwnd, cancellationToken);
        if (window is null)
        {
            Console.Error.WriteLine($"Unable to resolve HWND 0x{hwnd.ToInt64():X}.");
            return ProbeExitCode.TargetUnavailable;
        }

        ProbePrinter.PrintWindow(window);

        var imm = new LegacyImeOpenStatusAccessor();
        var before = imm.GetOpenStatus(window.Hwnd);
        PrintStatus("Before", before);

        if (!before.Success)
        {
            return ProbeExitCode.BackendFailure;
        }

        var requestedOpen = mode == "chinese";
        var setResult = imm.SetOpenStatus(window.Hwnd, requestedOpen);
        Console.WriteLine($"Set request: {(requestedOpen ? "Open (Chinese candidate)" : "Closed (English candidate)")}");
        Console.WriteLine($"Set result: {(setResult.Success ? "native call succeeded" : "FAILED")}");
        if (!setResult.Success)
        {
            Console.WriteLine($"Set error: {setResult.Error} (Win32={setResult.NativeError})");
            return ProbeExitCode.BackendFailure;
        }

        await Task.Delay(25, cancellationToken);
        var after = imm.GetOpenStatus(window.Hwnd);
        PrintStatus("After", after);

        if (!after.Success || after.IsOpen != requestedOpen)
        {
            Console.Error.WriteLine("Verification failed: the observed open status does not match the requested state.");
            return ProbeExitCode.BackendFailure;
        }

        Console.WriteLine("Verification: observed IMM open status matches the request.");
        Console.WriteLine("P0 note: confirm visually that this corresponds to Microsoft Pinyin Chinese/English before promoting it to the production backend.");
        return ProbeExitCode.Success;
    }

    private static void PrintStatus(string label, ImeOpenStatusResult status)
    {
        var value = status.Success ? (status.IsOpen ? "Open" : "Closed") : "<read failed>";
        Console.WriteLine($"{label} IMM status: {value}");
        if (!status.Success)
        {
            Console.WriteLine($"{label} error: {status.Error} (Win32={status.NativeError})");
        }
    }

    private static bool TryReadMode(IReadOnlyList<string> args, out string mode)
    {
        mode = string.Empty;
        for (var index = 0; index < args.Count; index++)
        {
            if (!args[index].Equals("--mode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Count)
            {
                return false;
            }

            mode = args[index + 1].ToLowerInvariant();
            return mode is "chinese" or "english";
        }

        return false;
    }
}
