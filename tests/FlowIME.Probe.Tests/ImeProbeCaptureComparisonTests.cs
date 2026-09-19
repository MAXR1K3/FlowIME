using FlowIME.Probe;

namespace FlowIME.Probe.Tests;

public sealed class ImeProbeCaptureComparisonTests
{
    private static readonly Guid WeChatClsid = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid WeChatProfile = new("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

    [Fact]
    public void Analyze_detects_stable_conversion_mode_difference()
    {
        var left = Capture("wechat-chinese", WeChatClsid, WeChatProfile, 0x401, true);
        var right = Capture("wechat-english", WeChatClsid, WeChatProfile, 0x000, true);

        var result = ImeProbeCaptureComparison.Analyze(left, right);

        Assert.Equal(ImeProbeDistinction.ConversionMode, result.Distinction);
        Assert.True(result.SameProfile);
        Assert.Equal((uint?)0x401, result.LeftConversionMode);
        Assert.Equal((uint?)0x000, result.RightConversionMode);
    }

    [Fact]
    public void Analyze_detects_open_status_difference_when_conversion_is_same()
    {
        var left = Capture("left", WeChatClsid, WeChatProfile, 0x000, true);
        var right = Capture("right", WeChatClsid, WeChatProfile, 0x000, false);

        var result = ImeProbeCaptureComparison.Analyze(left, right);

        Assert.Equal(ImeProbeDistinction.OpenStatus, result.Distinction);
        Assert.True(result.SameProfile);
    }

    [Fact]
    public void Analyze_rejects_profile_change_between_captures()
    {
        var left = Capture("left", WeChatClsid, WeChatProfile, 0x000, true);
        var right = Capture(
            "right",
            new Guid("99999999-2222-3333-4444-555555555555"),
            WeChatProfile,
            0x000,
            true);

        var result = ImeProbeCaptureComparison.Analyze(left, right);

        Assert.Equal(ImeProbeDistinction.ProfileChanged, result.Distinction);
        Assert.False(result.SameProfile);
    }

    [Fact]
    public void Analyze_rejects_capture_that_lost_foreground()
    {
        var left = Capture("left", WeChatClsid, WeChatProfile, 0x000, true, foreground: false);
        var right = Capture("right", WeChatClsid, WeChatProfile, 0x000, true);

        var result = ImeProbeCaptureComparison.Analyze(left, right);

        Assert.Equal(ImeProbeDistinction.InvalidCapture, result.Distinction);
        Assert.False(result.LeftValid);
    }

    [Fact]
    public void Analyze_marks_unstable_conversion_values()
    {
        var left = Capture(
            "left",
            WeChatClsid,
            WeChatProfile,
            conversionMode: 0x000,
            isOpen: true,
            alternateConversionMode: 0x401);
        var right = Capture("right", WeChatClsid, WeChatProfile, 0x000, true);

        var result = ImeProbeCaptureComparison.Analyze(left, right);

        Assert.Equal(ImeProbeDistinction.Unstable, result.Distinction);
        Assert.False(result.LeftStable);
    }

    private static ImeProbeCaptureDocument Capture(
        string label,
        Guid clsid,
        Guid profileGuid,
        uint conversionMode,
        bool isOpen,
        bool foreground = true,
        uint? alternateConversionMode = null)
    {
        var samples = Enumerable.Range(0, 5)
            .Select(index => Sample(
                clsid,
                profileGuid,
                alternateConversionMode.HasValue && index == 4
                    ? alternateConversionMode.Value
                    : conversionMode,
                isOpen,
                foreground))
            .ToArray();

        return new ImeProbeCaptureDocument(
            1,
            DateTimeOffset.UtcNow,
            label,
            new ImeProbeWindowIdentity(
                "notepad",
                42,
                @"C:\\Windows\\System32\\notepad.exe",
                "Notepad",
                null,
                "0x1234"),
            samples);
    }

    private static ImeProbeSample Sample(
        Guid clsid,
        Guid profileGuid,
        uint conversionMode,
        bool isOpen,
        bool foreground) =>
        new(
            DateTimeOffset.UtcNow,
            foreground,
            foreground ? "0x1234" : "0x9999",
            new ImeProbeFocusSnapshot(
                7,
                "0x1234",
                "0x2345",
                "0x2345",
                "0x2345",
                "Focus",
                true,
                0,
                null),
            "0x8040804",
            new ImeProbeOpenStatusSnapshot(true, isOpen, "0x3456", 0, null),
            new ImeProbeConversionSnapshot(true, conversionMode, (conversionMode & 1) != 0, "0x3456", 0, null),
            new ImeProbeProfileSnapshot(
                true,
                0,
                1,
                0x0804,
                clsid,
                profileGuid,
                Guid.Empty,
                "0x0",
                "0x0",
                0,
                0,
                null));
}
