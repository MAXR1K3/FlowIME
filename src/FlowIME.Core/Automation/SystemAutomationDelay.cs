namespace FlowIME.Core.Automation;

public sealed class SystemAutomationDelay : IAutomationDelay
{
    public static SystemAutomationDelay Instance { get; } = new();

    private SystemAutomationDelay()
    {
    }

    public ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default) =>
        new(Task.Delay(delay, cancellationToken));
}
