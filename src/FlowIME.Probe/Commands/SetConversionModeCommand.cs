using FlowIME.Windows.Input;
using FlowIME.Windows.Windowing;

namespace FlowIME.Probe.Commands;

internal static class SetConversionModeCommand
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

        var imm = new LegacyImeConversionModeAccessor();
        var before = imm.GetConversionMode(window.Hwnd);
        PrintMode("Before", before);
        if (!before.Success)
        {
            return ProbeExitCode.BackendFailure;
        }

        var requestChinese = mode == "chinese";
        var requestedMode = requestChinese
            ? before.ConversionMode | LegacyImeConversionModeAccessor.ImeCmodeNative
            : before.ConversionMode & ~LegacyImeConversionModeAccessor.ImeCmodeNative;

        var setResult = imm.SetConversionMode(window.Hwnd, requestedMode);
        Console.WriteLine($"Set request: 0x{requestedMode:X8} ({(requestChinese ? "NATIVE bit ON / Chinese candidate" : "NATIVE bit OFF / English candidate")})");
        Console.WriteLine($"Set result: {(setResult.Success ? "native call succeeded" : "FAILED")}");
        if (!setResult.Success)
        {
            Console.WriteLine($"Set error: {setResult.Error} (Win32={setResult.NativeError})");
            return ProbeExitCode.BackendFailure;
        }

        await Task.Delay(25, cancellationToken);
        var after = imm.GetConversionMode(window.Hwnd);
        PrintMode("After", after);
        if (!after.Success)
        {
            return ProbeExitCode.BackendFailure;
        }

        var observedChinese = after.IsNative;
        if (observedChinese != requestChinese)
        {
            Console.Error.WriteLine("Verification failed: IME_CMODE_NATIVE does not match the requested candidate state.");
            return ProbeExitCode.BackendFailure;
        }

        Console.WriteLine("Verification: IME_CMODE_NATIVE matches the request.");
        Console.WriteLine("P0 note: this is still experimental. Confirm actual text input visually before treating NATIVE as Microsoft Pinyin Chinese/English.");
        return ProbeExitCode.Success;
    }

    private static void PrintMode(string label, ImeConversionModeResult result)
    {
        if (!result.Success)
        {
            Console.WriteLine($"{label} IMM conversion mode: <read failed>");
            Console.WriteLine($"{label} error: {result.Error} (Win32={result.NativeError})");
            return;
        }

        Console.WriteLine($"{label} IMM conversion mode: 0x{result.ConversionMode:X8}");
        Console.WriteLine($"{label} IME_CMODE_NATIVE: {(result.IsNative ? "On" : "Off")}");
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
