namespace FlowIME.Core.Automation;

public interface IAutomationDelay
{
    ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}
