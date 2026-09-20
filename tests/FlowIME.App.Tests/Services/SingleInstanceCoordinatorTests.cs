using FlowIME.App.Services;

namespace FlowIME.App.Tests.Services;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void Secondary_instance_signals_primary_listener()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\FlowIME.Tests.Mutex.{suffix}";
        var eventName = $@"Local\FlowIME.Tests.Event.{suffix}";

        using var primary = new SingleInstanceCoordinator(mutexName, eventName);
        using var signaled = new ManualResetEventSlim(false);
        Assert.True(primary.IsPrimary);
        primary.StartListening(signaled.Set);

        bool? secondaryWasPrimary = null;
        Exception? secondaryError = null;
        var secondaryThread = new Thread(() =>
        {
            try
            {
                using var secondary = new SingleInstanceCoordinator(mutexName, eventName);
                secondaryWasPrimary = secondary.IsPrimary;
                secondary.SignalPrimary();
            }
            catch (Exception ex)
            {
                secondaryError = ex;
            }
        });

        secondaryThread.Start();
        Assert.True(secondaryThread.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(secondaryError);
        Assert.False(secondaryWasPrimary);
        Assert.True(signaled.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Secondary_instance_can_request_primary_exit()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\FlowIME.Tests.Mutex.{suffix}";
        var showEventName = $@"Local\FlowIME.Tests.ShowEvent.{suffix}";
        var exitEventName = $@"Local\FlowIME.Tests.ExitEvent.{suffix}";

        using var primary = new SingleInstanceCoordinator(
            mutexName,
            showEventName,
            exitEventName);
        using var showSignaled = new ManualResetEventSlim(false);
        using var exitSignaled = new ManualResetEventSlim(false);
        primary.StartListening(showSignaled.Set, exitSignaled.Set);

        var secondaryThread = new Thread(() =>
        {
            using var secondary = new SingleInstanceCoordinator(
                mutexName,
                showEventName,
                exitEventName);
            secondary.SignalPrimaryExit();
        });

        secondaryThread.Start();
        Assert.True(secondaryThread.Join(TimeSpan.FromSeconds(2)));
        Assert.True(exitSignaled.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.False(showSignaled.IsSet);
    }
}
