using FlowIME.Core.Abstractions;

namespace FlowIME.Probe;

internal static class ProbeTargetParser
{
    internal static bool TryResolve(
        IReadOnlyList<string> args,
        IForegroundWindowSource foreground,
        out nint hwnd,
        out string? error)
    {
        hwnd = 0;
        error = null;

        var foregroundCount = args.Count(arg =>
            arg.Equals("--foreground", StringComparison.OrdinalIgnoreCase));
        var hwndOptionIndexes = Enumerable.Range(0, args.Count)
            .Where(index => args[index].Equals("--hwnd", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (foregroundCount + hwndOptionIndexes.Length != 1)
        {
            error = "Specify exactly one target using --foreground or --hwnd <value>.";
            return false;
        }

        if (foregroundCount == 1)
        {
            hwnd = foreground.GetCurrentForegroundWindow();
            if (hwnd == 0)
            {
                error = "There is no foreground window.";
                return false;
            }

            return true;
        }

        var hwndIndex = hwndOptionIndexes[0];
        if (hwndIndex + 1 >= args.Count || !HwndParser.TryParse(args[hwndIndex + 1], out hwnd))
        {
            error = "--hwnd requires a non-zero decimal or 0x-prefixed hexadecimal HWND.";
            return false;
        }

        return true;
    }
}
