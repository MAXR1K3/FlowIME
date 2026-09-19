using FlowIME.Core.Context;
using FlowIME.Core.Models;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Context;

internal interface IUserNotificationStateProbe
{
    QueryUserNotificationState? Capture();
}

internal sealed class ShellUserNotificationStateProbe : IUserNotificationStateProbe
{
    public QueryUserNotificationState? Capture()
    {
        var result = Shell32Native.SHQueryUserNotificationState(out var state);
        return result >= 0 ? state : null;
    }
}

/// <summary>
/// Uses the Shell notification state only as strong game evidence when Windows says
/// an exclusive-mode Direct3D fullscreen application is active. QUNS_BUSY is
/// deliberately not treated as a game because it also covers ordinary fullscreen
/// applications and presentation scenarios.
/// </summary>
internal sealed class Direct3DFullscreenEvidenceProbe : IGameplayEvidenceProbe
{
    internal const string ProbeId = "windows.gameplay.direct3d-exclusive";

    private readonly IUserNotificationStateProbe _probe;

    public Direct3DFullscreenEvidenceProbe()
        : this(new ShellUserNotificationStateProbe())
    {
    }

    internal Direct3DFullscreenEvidenceProbe(IUserNotificationStateProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public string Id => ProbeId;

    public int Order => 200;

    public GameplayEvidence? Capture(WindowContext window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return _probe.Capture() == QueryUserNotificationState.RunningDirect3DFullScreen
            ? new GameplayEvidence(
                GameplayEvidenceKind.Direct3DExclusive,
                ProbeId,
                ContextSignalConfidence.High)
            : null;
    }
}
