namespace FlowIME.Probe;

internal enum ImeProbeDistinction
{
    InvalidCapture,
    ProfileChanged,
    Unstable,
    NoneObserved,
    ConversionMode,
    OpenStatus,
    ConversionModeAndOpenStatus
}

internal sealed record ImeProbeComparisonResult(
    ImeProbeDistinction Distinction,
    bool SameProfile,
    bool LeftValid,
    bool RightValid,
    bool LeftStable,
    bool RightStable,
    uint? LeftConversionMode,
    uint? RightConversionMode,
    bool? LeftOpenStatus,
    bool? RightOpenStatus,
    Guid LeftClsid,
    Guid RightClsid,
    Guid LeftProfileGuid,
    Guid RightProfileGuid,
    string Summary);

internal static class ImeProbeCaptureComparison
{
    internal static ImeProbeComparisonResult Analyze(
        ImeProbeCaptureDocument left,
        ImeProbeCaptureDocument right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftFacts = CaptureFacts.Create(left);
        var rightFacts = CaptureFacts.Create(right);

        if (!string.Equals(
                left.Window.ProcessName,
                right.Window.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                ImeProbeDistinction.InvalidCapture,
                leftFacts,
                rightFacts,
                "The captures use different host processes. Repeat Chinese and English captures in the same application and text host.");
        }

        if (!leftFacts.Valid || !rightFacts.Valid)
        {
            return Result(
                ImeProbeDistinction.InvalidCapture,
                leftFacts,
                rightFacts,
                "At least one capture is invalid. Keep the target application foreground for every sample and repeat the capture.");
        }

        var sameProfile =
            leftFacts.Clsid == rightFacts.Clsid &&
            leftFacts.ProfileGuid == rightFacts.ProfileGuid &&
            leftFacts.LanguageId == rightFacts.LanguageId;

        if (!sameProfile)
        {
            return Result(
                ImeProbeDistinction.ProfileChanged,
                leftFacts,
                rightFacts,
                "The active TSF profile changed between captures. This compares two input profiles, not two internal modes of one provider.");
        }

        if (!leftFacts.Stable || !rightFacts.Stable)
        {
            return Result(
                ImeProbeDistinction.Unstable,
                leftFacts,
                rightFacts,
                "At least one capture was not stable across samples. Repeat after the target input control has settled.");
        }

        var conversionDiffers =
            leftFacts.ConversionMode.HasValue &&
            rightFacts.ConversionMode.HasValue &&
            leftFacts.ConversionMode.Value != rightFacts.ConversionMode.Value;

        var openDiffers =
            leftFacts.OpenStatus.HasValue &&
            rightFacts.OpenStatus.HasValue &&
            leftFacts.OpenStatus.Value != rightFacts.OpenStatus.Value;

        var distinction = (conversionDiffers, openDiffers) switch
        {
            (true, true) => ImeProbeDistinction.ConversionModeAndOpenStatus,
            (true, false) => ImeProbeDistinction.ConversionMode,
            (false, true) => ImeProbeDistinction.OpenStatus,
            _ => ImeProbeDistinction.NoneObserved
        };

        var summary = distinction switch
        {
            ImeProbeDistinction.ConversionModeAndOpenStatus =>
                "Both IMM conversion mode and IMM open status distinguish the two observed states. Mutation still requires a provider-specific write/verify experiment.",
            ImeProbeDistinction.ConversionMode =>
                "IMM conversion mode distinguishes the two observed states; IMM open status does not. This is the safest next mutation candidate.",
            ImeProbeDistinction.OpenStatus =>
                "IMM open status distinguishes the two observed states; conversion mode does not. Open status is the next mutation candidate.",
            _ =>
                "No stable IMM open/conversion difference was observed. The provider may keep Chinese/English state outside the legacy IMM signals FlowIME currently reads."
        };

        return Result(distinction, leftFacts, rightFacts, summary);
    }

    private static ImeProbeComparisonResult Result(
        ImeProbeDistinction distinction,
        CaptureFacts left,
        CaptureFacts right,
        string summary) =>
        new(
            distinction,
            left.SameProfileIdentityAs(right),
            left.Valid,
            right.Valid,
            left.Stable,
            right.Stable,
            left.ConversionMode,
            right.ConversionMode,
            left.OpenStatus,
            right.OpenStatus,
            left.Clsid,
            right.Clsid,
            left.ProfileGuid,
            right.ProfileGuid,
            summary);

    private sealed record CaptureFacts(
        bool Valid,
        bool Stable,
        uint? ConversionMode,
        bool? OpenStatus,
        ushort LanguageId,
        Guid Clsid,
        Guid ProfileGuid)
    {
        internal bool SameProfileIdentityAs(CaptureFacts other) =>
            Clsid == other.Clsid &&
            ProfileGuid == other.ProfileGuid &&
            LanguageId == other.LanguageId;

        internal static CaptureFacts Create(ImeProbeCaptureDocument document)
        {
            var foregroundSamples = document.Samples
                .Where(sample => sample.TargetIsForeground)
                .ToArray();

            var valid =
                document.Samples.Count >= 3 &&
                foregroundSamples.Length == document.Samples.Count &&
                foregroundSamples.All(sample => sample.ActiveProfile.Success);

            var profiles = foregroundSamples
                .Where(sample => sample.ActiveProfile.Success)
                .Select(sample => new
                {
                    sample.ActiveProfile.LanguageId,
                    sample.ActiveProfile.Clsid,
                    sample.ActiveProfile.ProfileGuid
                })
                .Distinct()
                .ToArray();

            var profileStable = profiles.Length == 1;
            var profile = profileStable ? profiles[0] : null;

            var conversionReadable = foregroundSamples
                .Where(sample => sample.ConversionMode.Success && sample.ConversionMode.ConversionMode.HasValue)
                .ToArray();
            var conversionModes = conversionReadable
                .Select(sample => sample.ConversionMode.ConversionMode!.Value)
                .Distinct()
                .ToArray();

            var openReadable = foregroundSamples
                .Where(sample => sample.OpenStatus.Success && sample.OpenStatus.IsOpen.HasValue)
                .ToArray();
            var openStatuses = openReadable
                .Select(sample => sample.OpenStatus.IsOpen!.Value)
                .Distinct()
                .ToArray();

            var conversionComplete =
                foregroundSamples.Length > 0 &&
                conversionReadable.Length == foregroundSamples.Length &&
                conversionModes.Length == 1;
            var openComplete =
                foregroundSamples.Length > 0 &&
                openReadable.Length == foregroundSamples.Length &&
                openStatuses.Length == 1;
            var conversionStable = conversionModes.Length <= 1;
            var openStable = openStatuses.Length <= 1;
            var haveCompleteReadableSignal = conversionComplete || openComplete;
            var stable =
                valid &&
                profileStable &&
                conversionStable &&
                openStable &&
                haveCompleteReadableSignal;

            return new CaptureFacts(
                valid,
                stable,
                conversionComplete ? conversionModes[0] : null,
                openComplete ? openStatuses[0] : null,
                profile?.LanguageId ?? 0,
                profile?.Clsid ?? Guid.Empty,
                profile?.ProfileGuid ?? Guid.Empty);
        }
    }
}
