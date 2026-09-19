using System.Text.Json;
using FlowIME.Windows.Input;
using FlowIME.Windows.Windowing;

namespace FlowIME.Probe.Commands;

internal static class CaptureCommand
{
    private const int DefaultSamples = 5;
    private const int DefaultIntervalMilliseconds = 75;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!TryReadIntOption(args, "--delay-ms", 0, 30_000, 0, out var delayMilliseconds, out var error) ||
            !TryReadIntOption(args, "--samples", 1, 20, DefaultSamples, out var sampleCount, out error) ||
            !TryReadIntOption(args, "--interval-ms", 0, 2_000, DefaultIntervalMilliseconds, out var intervalMilliseconds, out error))
        {
            Console.Error.WriteLine(error);
            return ProbeExitCode.InvalidArguments;
        }

        var label = ReadStringOption(args, "--label") ?? "capture";
        if (string.IsNullOrWhiteSpace(label))
        {
            Console.Error.WriteLine("--label cannot be empty.");
            return ProbeExitCode.InvalidArguments;
        }

        if (delayMilliseconds > 0)
        {
            Console.WriteLine($"Capturing foreground target in {delayMilliseconds} ms.");
            Console.WriteLine("Switch to the target app, focus a real text field, and keep it foreground until capture completes...");
            await Task.Delay(delayMilliseconds, cancellationToken);
        }

        using var foreground = new ForegroundWindowSource();
        if (!ProbeTargetParser.TryResolve(args, foreground, out var hwnd, out error))
        {
            Console.Error.WriteLine(error);
            return ProbeExitCode.InvalidArguments;
        }

        var resolver = new WindowResolver();
        var initialWindow = await resolver.ResolveAsync(hwnd, cancellationToken);
        if (initialWindow is null)
        {
            Console.Error.WriteLine($"Unable to resolve HWND {FormatHwnd(hwnd)}.");
            return ProbeExitCode.TargetUnavailable;
        }

        var focusResolver = new FocusWindowResolver();
        var layoutInspector = new KeyboardLayoutInspector();
        var openStatusAccessor = new LegacyImeOpenStatusAccessor();
        var conversionModeAccessor = new LegacyImeConversionModeAccessor();
        var profileInspector = new TsfInputProfileInspector();

        var samples = new List<ImeProbeSample>(sampleCount);
        for (var index = 0; index < sampleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var window = await resolver.ResolveAsync(hwnd, cancellationToken) ?? initialWindow;
            var foregroundHwnd = foreground.GetCurrentForegroundWindow();
            var focus = focusResolver.Resolve(window.Hwnd);
            var inputTarget = focus.EffectiveInputWindow != 0
                ? focus.EffectiveInputWindow
                : window.Hwnd;
            var inputThreadId = focus.ThreadId != 0 ? focus.ThreadId : window.ThreadId;
            var keyboardLayout = layoutInspector.GetKeyboardLayout(inputThreadId);
            var openStatus = openStatusAccessor.GetOpenStatus(inputTarget);
            var conversion = conversionModeAccessor.GetConversionMode(inputTarget);
            var profile = profileInspector.GetActiveKeyboardProfile();

            samples.Add(new ImeProbeSample(
                DateTimeOffset.UtcNow,
                foregroundHwnd == window.Hwnd,
                FormatHwnd(foregroundHwnd),
                new ImeProbeFocusSnapshot(
                    focus.ThreadId,
                    FormatHwnd(focus.ActiveWindow),
                    FormatHwnd(focus.FocusWindow),
                    FormatHwnd(focus.CaretWindow),
                    FormatHwnd(focus.EffectiveInputWindow),
                    focus.Source.ToString(),
                    focus.GuiThreadInfoAvailable,
                    focus.NativeError,
                    focus.Error),
                FormatHwnd(keyboardLayout),
                new ImeProbeOpenStatusSnapshot(
                    openStatus.Success,
                    openStatus.Success ? openStatus.IsOpen : null,
                    FormatHwnd(openStatus.ImeWindow),
                    openStatus.NativeError,
                    openStatus.Error),
                new ImeProbeConversionSnapshot(
                    conversion.Success,
                    conversion.Success ? conversion.ConversionMode : null,
                    conversion.Success ? conversion.IsNative : null,
                    FormatHwnd(conversion.ImeWindow),
                    conversion.NativeError,
                    conversion.Error),
                new ImeProbeProfileSnapshot(
                    profile.Success,
                    profile.HResult,
                    profile.ProfileType,
                    profile.LanguageId,
                    profile.Clsid,
                    profile.ProfileGuid,
                    profile.CategoryId,
                    FormatHwnd(profile.KeyboardLayout),
                    FormatHwnd(profile.SubstituteKeyboardLayout),
                    profile.Capabilities,
                    profile.Flags,
                    profile.Error)));

            if (index + 1 < sampleCount && intervalMilliseconds > 0)
            {
                await Task.Delay(intervalMilliseconds, cancellationToken);
            }
        }

        var capture = new ImeProbeCaptureDocument(
            SchemaVersion: 1,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Label: label,
            Window: new ImeProbeWindowIdentity(
                initialWindow.ProcessName,
                initialWindow.ProcessId,
                initialWindow.ExecutablePath,
                initialWindow.WindowClass ?? string.Empty,
                initialWindow.PackageFamilyName,
                FormatHwnd(initialWindow.Hwnd)),
            Samples: samples);

        var outputPath = ResolveOutputPath(args, label);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(capture, JsonOptions),
            cancellationToken);

        PrintSummary(capture, outputPath);
        return ProbeExitCode.Success;
    }

    private static void PrintSummary(ImeProbeCaptureDocument capture, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine($"Capture: {capture.Label}");
        Console.WriteLine($"Process: {capture.Window.ProcessName} ({capture.Window.ProcessId})");
        Console.WriteLine($"HWND: {capture.Window.TopLevelHwnd}");
        Console.WriteLine($"Samples: {capture.Samples.Count}");
        Console.WriteLine($"Target foreground on every sample: {(capture.Samples.All(sample => sample.TargetIsForeground) ? "yes" : "NO")}");

        var profiles = capture.Samples
            .Where(sample => sample.ActiveProfile.Success)
            .Select(sample => $"{sample.ActiveProfile.Clsid:B} / {sample.ActiveProfile.ProfileGuid:B} / LANG=0x{sample.ActiveProfile.LanguageId:X4}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Console.WriteLine($"TSF profile(s): {(profiles.Length == 0 ? "<unavailable>" : string.Join(" | ", profiles))}");

        var openValues = capture.Samples
            .Where(sample => sample.OpenStatus.Success && sample.OpenStatus.IsOpen.HasValue)
            .Select(sample => sample.OpenStatus.IsOpen!.Value ? "Open" : "Closed")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"IMM open status: {(openValues.Length == 0 ? "<unavailable>" : string.Join(", ", openValues))}");

        var conversionValues = capture.Samples
            .Where(sample => sample.ConversionMode.Success && sample.ConversionMode.ConversionMode.HasValue)
            .Select(sample => $"0x{sample.ConversionMode.ConversionMode!.Value:X8}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"IMM conversion mode: {(conversionValues.Length == 0 ? "<unavailable>" : string.Join(", ", conversionValues))}");
        Console.WriteLine($"Saved: {outputPath}");
        Console.WriteLine("Safety: capture is read-only; no IME state was changed.");
    }

    private static string ResolveOutputPath(IReadOnlyList<string> args, string label)
    {
        var explicitPath = ReadStringOption(args, "--out");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safeLabel = new string(label
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();
        if (string.IsNullOrWhiteSpace(safeLabel))
        {
            safeLabel = "capture";
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowIME",
            "probe");
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
        return Path.Combine(root, $"{safeLabel}-{stamp}.json");
    }

    private static bool TryReadIntOption(
        IReadOnlyList<string> args,
        string option,
        int minimum,
        int maximum,
        int defaultValue,
        out int value,
        out string? error)
    {
        value = defaultValue;
        error = null;
        var indexes = Enumerable.Range(0, args.Count)
            .Where(index => args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (indexes.Length > 1)
        {
            error = $"Specify {option} at most once.";
            return false;
        }

        if (indexes.Length == 0)
        {
            return true;
        }

        var index = indexes[0];
        if (index + 1 >= args.Count ||
            !int.TryParse(args[index + 1], out value) ||
            value < minimum ||
            value > maximum)
        {
            error = $"{option} must be an integer from {minimum} through {maximum}.";
            return false;
        }

        return true;
    }

    private static string? ReadStringOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return index + 1 < args.Count ? args[index + 1] : null;
        }

        return null;
    }

    private static string FormatHwnd(nint hwnd) =>
        hwnd == 0 ? "0x0" : $"0x{hwnd.ToInt64():X}";
}
