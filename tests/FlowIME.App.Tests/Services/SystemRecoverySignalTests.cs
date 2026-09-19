using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class SystemRecoverySignalTests
{
    [Fact]
    public void Resume_and_unlock_messages_request_recovery()
    {
        var signals = new (uint Message, nuint Parameter)[]
        {
            (SystemRecoverySignal.WmPowerBroadcast, SystemRecoverySignal.PbtApmResumeSuspend),
            (SystemRecoverySignal.WmPowerBroadcast, SystemRecoverySignal.PbtApmResumeAutomatic),
            (SystemRecoverySignal.WmWtsSessionChange, SystemRecoverySignal.WtsSessionLogon),
            (SystemRecoverySignal.WmWtsSessionChange, SystemRecoverySignal.WtsSessionUnlock)
        };

        foreach (var signal in signals)
        {
            Assert.True(SystemRecoverySignal.IsRecoveryMessage(signal.Message, signal.Parameter));
            Assert.NotEqual(
                "unknown",
                SystemRecoverySignal.Describe(signal.Message, signal.Parameter));
        }
    }

    [Fact]
    public void Unrelated_messages_do_not_request_recovery()
    {
        var signals = new (uint Message, nuint Parameter)[]
        {
            (SystemRecoverySignal.WmPowerBroadcast, 0x0004),
            (SystemRecoverySignal.WmWtsSessionChange, 0x0007),
            (0x0010, 0x0000)
        };

        foreach (var signal in signals)
        {
            Assert.False(SystemRecoverySignal.IsRecoveryMessage(signal.Message, signal.Parameter));
        }
    }
}
