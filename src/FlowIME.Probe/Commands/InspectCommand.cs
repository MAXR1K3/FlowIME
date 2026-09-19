using FlowIME.Windows.Input;
using FlowIME.Windows.Windowing;

namespace FlowIME.Probe.Commands;

internal static class InspectCommand
{
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryReadDelay(args, out var delayMilliseconds, out var delayError))
        {
            Console.Error.WriteLine(delayError);
            return ProbeExitCode.InvalidArguments;
        }

        if (delayMilliseconds > 0)
        {
            Console.WriteLine($"Sampling foreground target in {delayMilliseconds} ms. Switch to the app and leave its text control focused...");
            await Task.Delay(delayMilliseconds, cancellationToken);
        }

        using var foreground = new ForegroundWindowSource();
        if (!ProbeTargetParser.TryResolve(args, foreground, out var hwnd, out var error))
        {
            Console.Error.WriteLine(error);
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

        var focus = new FocusWindowResolver().Resolve(window.Hwnd);
        PrintFocusTarget(focus);
        var inputTarget = focus.EffectiveInputWindow != 0
            ? focus.EffectiveInputWindow
            : window.Hwnd;

        var layoutInspector = new KeyboardLayoutInspector();
        var keyboardLayout = layoutInspector.GetKeyboardLayout(window.ThreadId);
        Console.WriteLine($"KeyboardLayout: 0x{keyboardLayout.ToInt64():X}");

        var imm = new LegacyImeOpenStatusAccessor();
        var status = imm.GetOpenStatus(inputTarget);
        Console.WriteLine($"IMM default IME window: {(status.ImeWindow == 0 ? "<unavailable>" : $"0x{status.ImeWindow.ToInt64():X}")}");
        Console.WriteLine($"IMM open status: {(status.Success ? (status.IsOpen ? "Open" : "Closed") : "<read failed>")}");
        if (!status.Success)
        {
            Console.WriteLine($"IMM error: {status.Error} (Win32={status.NativeError})");
        }

        Console.WriteLine("P0 note: Open/Closed is experimental evidence. Do not label it Chinese/English until the Microsoft Pinyin matrix confirms the mapping.");

        var conversion = new LegacyImeConversionModeAccessor().GetConversionMode(inputTarget);
        Console.WriteLine($"IMM conversion mode: {(conversion.Success ? $"0x{conversion.ConversionMode:X8}" : "<read failed>")}");
        Console.WriteLine($"IMM IME_CMODE_NATIVE: {(conversion.Success ? (conversion.IsNative ? "On" : "Off") : "<unknown>")}");
        if (!conversion.Success)
        {
            Console.WriteLine($"IMM conversion error: {conversion.Error} (Win32={conversion.NativeError})");
        }
        Console.WriteLine("P0 note: IME_CMODE_NATIVE is a stronger Chinese/English candidate than OpenStatus, but still requires visual confirmation on modern Microsoft Pinyin.");

        Console.WriteLine();
        Console.WriteLine("TSF active keyboard profile (Probe process/thread context):");
        var profile = new TsfInputProfileInspector().GetActiveKeyboardProfile();
        if (profile.Success)
        {
            Console.WriteLine($"  ProfileType: 0x{profile.ProfileType:X}");
            Console.WriteLine($"  LanguageId: 0x{profile.LanguageId:X4}");
            Console.WriteLine($"  CLSID: {profile.Clsid:B}");
            Console.WriteLine($"  ProfileGuid: {profile.ProfileGuid:B}");
            Console.WriteLine($"  CategoryId: {profile.CategoryId:B}");
            Console.WriteLine($"  HKL: 0x{profile.KeyboardLayout.ToInt64():X}");
            Console.WriteLine($"  SubstituteHKL: 0x{profile.SubstituteKeyboardLayout.ToInt64():X}");
            Console.WriteLine($"  Caps: 0x{profile.Capabilities:X}");
            Console.WriteLine($"  Flags: 0x{profile.Flags:X}");
        }
        else
        {
            Console.WriteLine($"  <unavailable> HR=0x{profile.HResult:X8} {profile.Error}");
        }

        Console.WriteLine();
        Console.WriteLine("TSF compartments (Probe CURRENT THREAD MANAGER; not target app state):");
        var compartments = new TsfLocalCompartmentInspector().InspectCurrentThreadManager();
        Console.WriteLine($"  Scope: {compartments.Scope}");
        Console.WriteLine($"  ClientId: {compartments.ClientId}");
        Console.WriteLine($"  KEYBOARD_OPENCLOSE: {DisplayCompartment(compartments.KeyboardOpenClose)}");
        Console.WriteLine($"  INPUTMODE_CONVERSION: {DisplayCompartment(compartments.InputModeConversion)}");
        if (!compartments.Success)
        {
            Console.WriteLine($"  Error: {compartments.Error}");
        }

        return ProbeExitCode.Success;
    }

    private static void PrintFocusTarget(FocusWindowResult focus)
    {
        Console.WriteLine("Input focus resolution:");
        Console.WriteLine($"  GUI thread: {focus.ThreadId}");
        Console.WriteLine($"  hwndActive: {FormatHwnd(focus.ActiveWindow)}");
        Console.WriteLine($"  hwndFocus: {FormatHwnd(focus.FocusWindow)}");
        Console.WriteLine($"  hwndCaret: {FormatHwnd(focus.CaretWindow)}");
        Console.WriteLine($"  effective input HWND: {FormatHwnd(focus.EffectiveInputWindow)} ({focus.Source})");
        Console.WriteLine($"  GetGUIThreadInfo: {(focus.GuiThreadInfoAvailable ? "available" : "unavailable")}");
        if (!string.IsNullOrWhiteSpace(focus.Error))
        {
            Console.WriteLine($"  focus note: {focus.Error} (Win32={focus.NativeError})");
        }
        Console.WriteLine();
    }

    private static string FormatHwnd(nint hwnd) =>
        hwnd == 0 ? "<none>" : $"0x{hwnd.ToInt64():X}";

    private static bool TryReadDelay(
        IReadOnlyList<string> args,
        out int delayMilliseconds,
        out string? error)
    {
        delayMilliseconds = 0;
        error = null;

        for (var index = 0; index < args.Count; index++)
        {
            if (!args[index].Equals("--delay-ms", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

        return true;
    }

    private static string DisplayCompartment(int? value) =>
        value.HasValue ? $"0x{value.Value:X8} ({value.Value})" : "<unavailable>";
}
