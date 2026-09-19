using FlowIME.Core.Context;

namespace FlowIME.Windows.Context;

/// <summary>
/// Detects geometric fullscreen presentation for the current foreground window.
/// This detector deliberately does not decide that a fullscreen window is a game;
/// browsers, media players, presentations and remote-desktop clients may all be
/// fullscreen. P8B gameplay classification is layered on top of this signal.
/// </summary>
public sealed class FullscreenWindowDetector : IInputContextDetector
{
    internal const string DetectorId = "windows.fullscreen.geometry";
    internal const int DefaultEdgeTolerancePixels = 4;

    private readonly IWindowPresentationProbe _probe;

    public FullscreenWindowDetector()
        : this(new Win32WindowPresentationProbe())
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Fullscreen window detection requires Windows.");
        }
    }

    internal FullscreenWindowDetector(IWindowPresentationProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public string Id => DetectorId;

    public int Order => 100;

    public ValueTask<IReadOnlyList<InputContextSignal>> DetectAsync(
        ContextDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var presentation = _probe.Capture(request.Window.Hwnd);
        cancellationToken.ThrowIfCancellationRequested();

        if (!presentation.IsFullscreen(DefaultEdgeTolerancePixels))
        {
            return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
                Array.Empty<InputContextSignal>());
        }

        return ValueTask.FromResult<IReadOnlyList<InputContextSignal>>(
            [new(
                InputContextSignalKind.Fullscreen,
                DetectorId,
                ContextSignalConfidence.High)]);
    }
}
