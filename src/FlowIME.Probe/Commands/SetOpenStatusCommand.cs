using FlowIME.Windows.Input;
using FlowIME.Windows.Windowing;

namespace FlowIME.Probe.Commands;

/// <summary>
/// P5B discovery-only mutation probe for WeChat Input Method.
/// It deliberately refuses to write unless the active TSF profile exactly
/// matches the WeChat profile observed during read-only discovery.
/// </summary>
internal static class SetOpenStatusCommand
{
    private const int DefaultDelayMilliseconds = 0;
    private static readonly int[] VerificationDelaysMilliseconds = [25, 75, 100, 150];

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryReadMode(args, out var requestedOpen))
        {
            Console.Error.WriteLine("--mode must be either chinese or english.");
            return ProbeExitCode.InvalidArguments;
        }

        if (!TryReadDelay(args, out var delayMilliseconds, out var argumentError))
        {
            Console.Error.WriteLine(argumentError);
            return ProbeExitCode.InvalidArguments;
        }

        if (delayMilliseconds > 0)
        {
            Console.WriteLine($"Mutation probe starts in {delayMilliseconds} ms.");
            Console.WriteLine("Switch to Notepad, focus a real text field, select WeChat Input Method, and keep the window foreground...");
            await Task.Delay(delayMilliseconds, cancellationToken);
        }

        using var foreground = new ForegroundWindowSource();
        if (!ProbeTargetParser.TryResolve(args, foreground, out var hwnd, out var targetError))
        {
            Console.Error.WriteLine(targetError);
            return ProbeExitCode.InvalidArguments;
        }

        var resolver = new WindowResolver();
        var window = await resolver.ResolveAsync(hwnd, cancellationToken);
        if (window is null)
        {
            Console.Error.WriteLine($"Unable to resolve HWND {FormatHwnd(hwnd)}.");
            return ProbeExitCode.TargetUnavailable;
        }

        ProbePrinter.PrintWindow(window);

        if (foreground.GetCurrentForegroundWindow() != window.Hwnd)
        {
            Console.Error.WriteLine("Safety stop: target is no longer the foreground window.");
            return ProbeExitCode.TargetUnavailable;
        }

        var profileInspector = new TsfInputProfileInspector();
        var beforeProfile = profileInspector.GetActiveKeyboardProfile();
        PrintProfile("Before", beforeProfile);
        if (!WeChatInputMethodProfileIdentity.IsMatch(beforeProfile))
        {
            Console.Error.WriteLine("Safety stop: active TSF profile is not the WeChat Input Method profile observed during P5B discovery.");
            return ProbeExitCode.BackendFailure;
        }

        var focusResolver = new FocusWindowResolver();
        var beforeFocus = focusResolver.Resolve(window.Hwnd);
        PrintFocus("Before", beforeFocus);
        if (!beforeFocus.GuiThreadInfoAvailable ||
            beforeFocus.Source == FocusWindowSource.TopLevelFallback ||
            beforeFocus.EffectiveInputWindow == 0)
        {
            Console.Error.WriteLine("Safety stop: a real focused/caret input HWND could not be resolved. No mutation was sent.");
            return ProbeExitCode.TargetUnavailable;
        }

        var inputTarget = beforeFocus.EffectiveInputWindow;
        var openAccessor = new LegacyImeOpenStatusAccessor();
        var conversionAccessor = new LegacyImeConversionModeAccessor();
        var beforeOpen = openAccessor.GetOpenStatus(inputTarget);
        var beforeConversion = conversionAccessor.GetConversionMode(inputTarget);
        PrintOpenStatus("Before", beforeOpen);
        PrintConversion("Before", beforeConversion);

        if (!beforeOpen.Success)
        {
            Console.Error.WriteLine("Safety stop: current IMM open status could not be read. No mutation was sent.");
            return ProbeExitCode.BackendFailure;
        }

        if (beforeOpen.IsOpen == requestedOpen)
        {
            Console.Error.WriteLine(
                $"Mutation not exercised: WeChat is already {(requestedOpen ? "Open/Chinese candidate" : "Closed/English candidate")}. " +
                "Put it in the opposite state and repeat the probe.");
            return ProbeExitCode.BackendFailure;
        }

        Console.WriteLine();
        Console.WriteLine($"Mutation request: IMC_SETOPENSTATUS -> {(requestedOpen ? "Open (Chinese candidate)" : "Closed (English candidate)")}");
        var setResult = openAccessor.SetOpenStatus(inputTarget, requestedOpen);
        Console.WriteLine($"Native set result: {(setResult.Success ? "succeeded" : "FAILED")}");
        if (!setResult.Success)
        {
            Console.Error.WriteLine($"Set error: {setResult.Error} (Win32={setResult.NativeError})");
            return ProbeExitCode.BackendFailure;
        }

        var elapsedMilliseconds = 0;
        for (var index = 0; index < VerificationDelaysMilliseconds.Length; index++)
        {
            var delay = VerificationDelaysMilliseconds[index];
            elapsedMilliseconds += delay;
            await Task.Delay(delay, cancellationToken);

            if (foreground.GetCurrentForegroundWindow() != window.Hwnd)
            {
                Console.Error.WriteLine($"Verification {index + 1}: FAILED - foreground window changed.");
                return ProbeExitCode.TargetUnavailable;
            }

            var currentProfile = profileInspector.GetActiveKeyboardProfile();
            if (!WeChatInputMethodProfileIdentity.IsMatch(currentProfile))
            {
                Console.Error.WriteLine($"Verification {index + 1}: FAILED - active TSF profile changed.");
                PrintProfile("Current", currentProfile);
                return ProbeExitCode.BackendFailure;
            }

            var currentFocus = focusResolver.Resolve(window.Hwnd);
            if (!currentFocus.GuiThreadInfoAvailable ||
                currentFocus.Source == FocusWindowSource.TopLevelFallback ||
                currentFocus.EffectiveInputWindow == 0)
            {
                Console.Error.WriteLine($"Verification {index + 1}: FAILED - real input focus is unavailable.");
                return ProbeExitCode.TargetUnavailable;
            }

            if (currentFocus.EffectiveInputWindow != inputTarget)
            {
                Console.Error.WriteLine(
                    $"Verification {index + 1}: FAILED - focused input HWND changed from {FormatHwnd(inputTarget)} to {FormatHwnd(currentFocus.EffectiveInputWindow)}.");
                return ProbeExitCode.TargetUnavailable;
            }

            var currentOpen = openAccessor.GetOpenStatus(currentFocus.EffectiveInputWindow);
            if (!currentOpen.Success)
            {
                Console.Error.WriteLine($"Verification {index + 1}: FAILED - IMM open status could not be read.");
                PrintOpenStatus("Current", currentOpen);
                return ProbeExitCode.BackendFailure;
            }

            Console.WriteLine(
                $"Verification {index + 1} (+{elapsedMilliseconds} ms): " +
                $"foreground=yes focus=same profile=WeChat open={(currentOpen.IsOpen ? "Open" : "Closed")}");

            if (currentOpen.IsOpen != requestedOpen)
            {
                Console.Error.WriteLine("Verification failed: open status does not match the requested candidate state.");
                return ProbeExitCode.BackendFailure;
            }
        }

        var afterConversion = conversionAccessor.GetConversionMode(inputTarget);
        PrintConversion("After", afterConversion);
        Console.WriteLine();
        Console.WriteLine("PASS candidate: IMC_SETOPENSTATUS changed WeChat open status and the value remained stable across verification samples.");
        Console.WriteLine("Focus retained: yes. Foreground retained: yes. TSF profile retained: yes.");
        Console.WriteLine("Safety: FlowIME.Probe did not send text, keyboard shortcuts, or simulated key input.");
        Console.WriteLine("Manual confirmation is still required: type in Notepad and confirm the visible/input behavior matches the requested Chinese/English state.");
        return ProbeExitCode.Success;
    }

    private static void PrintOpenStatus(string label, ImeOpenStatusResult result)
    {
        if (!result.Success)
        {
            Console.WriteLine($"{label} IMM open status: <read failed>");
            Console.WriteLine($"{label} error: {result.Error} (Win32={result.NativeError})");
            return;
        }

        Console.WriteLine($"{label} IMM open status: {(result.IsOpen ? "Open" : "Closed")}");
        Console.WriteLine($"{label} IME HWND: {FormatHwnd(result.ImeWindow)}");
    }

    private static void PrintConversion(string label, ImeConversionModeResult result)
    {
        if (!result.Success)
        {
            Console.WriteLine($"{label} IMM conversion mode: <read failed>");
            return;
        }

        Console.WriteLine($"{label} IMM conversion mode: 0x{result.ConversionMode:X8}");
    }

    private static void PrintProfile(string label, TsfProfileSnapshot profile)
    {
        if (!profile.Success)
        {
            Console.WriteLine($"{label} TSF profile: <unavailable> ({profile.Error}, HRESULT=0x{profile.HResult:X8})");
            return;
        }

        Console.WriteLine(
            $"{label} TSF profile: {profile.Clsid:B} / {profile.ProfileGuid:B} / LANG=0x{profile.LanguageId:X4}");
    }

    private static void PrintFocus(string label, FocusWindowResult focus)
    {
        Console.WriteLine(
            $"{label} focus: source={focus.Source} effective={FormatHwnd(focus.EffectiveInputWindow)} " +
            $"focus={FormatHwnd(focus.FocusWindow)} caret={FormatHwnd(focus.CaretWindow)}");
    }

    private static bool TryReadMode(IReadOnlyList<string> args, out bool requestedOpen)
    {
        requestedOpen = false;
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

            var value = args[index + 1].ToLowerInvariant();
            if (value == "chinese")
            {
                requestedOpen = true;
                return true;
            }

            if (value == "english")
            {
                requestedOpen = false;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryReadDelay(
        IReadOnlyList<string> args,
        out int delayMilliseconds,
        out string? error)
    {
        delayMilliseconds = DefaultDelayMilliseconds;
        error = null;
        var indexes = Enumerable.Range(0, args.Count)
            .Where(index => args[index].Equals("--delay-ms", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (indexes.Length > 1)
        {
            error = "Specify --delay-ms at most once.";
            return false;
        }

        if (indexes.Length == 0)
        {
            return true;
        }

        var index = indexes[0];
        if (index + 1 >= args.Count ||
            !int.TryParse(args[index + 1], out delayMilliseconds) ||
            delayMilliseconds < 0 ||
            delayMilliseconds > 30_000)
        {
            error = "--delay-ms must be an integer from 0 through 30000.";
            return false;
        }

        return true;
    }

    private static string FormatHwnd(nint hwnd) =>
        hwnd == 0 ? "0x0" : $"0x{hwnd.ToInt64():X}";
}
