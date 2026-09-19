using System.Text.Json;

namespace FlowIME.Probe.Commands;

internal static class CompareCapturesCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var leftPath = ReadOption(args, "--left");
        var rightPath = ReadOption(args, "--right");
        if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
        {
            Console.Error.WriteLine("compare-captures requires --left <json> and --right <json>.");
            return ProbeExitCode.InvalidArguments;
        }

        var left = await LoadAsync(leftPath, cancellationToken);
        var right = await LoadAsync(rightPath, cancellationToken);
        if (left is null || right is null)
        {
            return ProbeExitCode.InvalidArguments;
        }

        var comparison = ImeProbeCaptureComparison.Analyze(left, right);
        Print(left, right, comparison);
        return comparison.Distinction is
            ImeProbeDistinction.InvalidCapture or
            ImeProbeDistinction.ProfileChanged or
            ImeProbeDistinction.Unstable
            ? ProbeExitCode.BackendFailure
            : ProbeExitCode.Success;
    }

    private static async Task<ImeProbeCaptureDocument?> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(expanded))
        {
            Console.Error.WriteLine($"Capture file not found: {expanded}");
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(expanded);
            var capture = await JsonSerializer.DeserializeAsync<ImeProbeCaptureDocument>(
                stream,
                JsonOptions,
                cancellationToken);
            if (capture is null || capture.SchemaVersion != 1)
            {
                Console.Error.WriteLine($"Unsupported or empty capture: {expanded}");
                return null;
            }

            return capture;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"Invalid capture JSON: {expanded}");
            Console.Error.WriteLine(exception.Message);
            return null;
        }
    }

    private static void Print(
        ImeProbeCaptureDocument left,
        ImeProbeCaptureDocument right,
        ImeProbeComparisonResult comparison)
    {
        Console.WriteLine("FlowIME IME capture comparison");
        Console.WriteLine();
        Console.WriteLine($"Left:  {left.Label} ({left.Window.ProcessName})");
        Console.WriteLine($"Right: {right.Label} ({right.Window.ProcessName})");
        Console.WriteLine($"Left valid/stable:  {YesNo(comparison.LeftValid)} / {YesNo(comparison.LeftStable)}");
        Console.WriteLine($"Right valid/stable: {YesNo(comparison.RightValid)} / {YesNo(comparison.RightStable)}");
        Console.WriteLine($"Same TSF profile: {YesNo(comparison.SameProfile)}");
        Console.WriteLine($"Left CLSID/Profile:  {comparison.LeftClsid:B} / {comparison.LeftProfileGuid:B}");
        Console.WriteLine($"Right CLSID/Profile: {comparison.RightClsid:B} / {comparison.RightProfileGuid:B}");
        Console.WriteLine($"Left conversion:  {FormatConversion(comparison.LeftConversionMode)}");
        Console.WriteLine($"Right conversion: {FormatConversion(comparison.RightConversionMode)}");
        Console.WriteLine($"Left open:  {FormatOpen(comparison.LeftOpenStatus)}");
        Console.WriteLine($"Right open: {FormatOpen(comparison.RightOpenStatus)}");
        Console.WriteLine();
        Console.WriteLine($"Observed distinction: {comparison.Distinction}");
        Console.WriteLine(comparison.Summary);
        Console.WriteLine("Safety: comparison is read-only and does not prove that either observed value is safe to write.");
    }

    private static string? ReadOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Count ? args[index + 1] : null;
            }
        }

        return null;
    }

    private static string YesNo(bool value) => value ? "yes" : "NO";

    private static string FormatConversion(uint? value) =>
        value.HasValue ? $"0x{value.Value:X8}" : "<unavailable/unstable>";

    private static string FormatOpen(bool? value) =>
        value.HasValue ? (value.Value ? "Open" : "Closed") : "<unavailable/unstable>";
}
