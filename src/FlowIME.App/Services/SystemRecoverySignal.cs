namespace FlowIME.App.Services;

internal static class SystemRecoverySignal
{
    internal const uint WmPowerBroadcast = 0x0218;
    internal const uint WmWtsSessionChange = 0x02B1;

    internal const nuint PbtApmResumeSuspend = 0x0007;
    internal const nuint PbtApmResumeAutomatic = 0x0012;

    internal const nuint WtsSessionLogon = 0x0005;
    internal const nuint WtsSessionUnlock = 0x0008;

    internal static bool IsRecoveryMessage(uint message, nuint wParam) =>
        message switch
        {
            WmPowerBroadcast =>
                wParam is PbtApmResumeSuspend or PbtApmResumeAutomatic,
            WmWtsSessionChange =>
                wParam is WtsSessionLogon or WtsSessionUnlock,
            _ => false
        };

    internal static string Describe(uint message, nuint wParam) =>
        (message, wParam) switch
        {
            (WmPowerBroadcast, PbtApmResumeSuspend) => "power-resume-suspend",
            (WmPowerBroadcast, PbtApmResumeAutomatic) => "power-resume-automatic",
            (WmWtsSessionChange, WtsSessionLogon) => "session-logon",
            (WmWtsSessionChange, WtsSessionUnlock) => "session-unlock",
            _ => "unknown"
        };
}
