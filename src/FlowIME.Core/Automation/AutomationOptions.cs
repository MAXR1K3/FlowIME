namespace FlowIME.Core.Automation;

public sealed record AutomationOptions
{
    /// <summary>
    /// Debounce applied to every foreground/focus re-apply request. The delay
    /// coalesces the burst of WinEvent notifications produced while a window and
    /// its first text control are settling.
    /// </summary>
    public TimeSpan ForegroundDebounce { get; init; } =
        TimeSpan.FromMilliseconds(75);

    /// <summary>
    /// Delay before the second apply attempt.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } =
        TimeSpan.FromMilliseconds(35);

    /// <summary>
    /// Delay before the third and any later bounded apply attempts.
    /// </summary>
    public TimeSpan FinalRetryDelay { get; init; } =
        TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Retained for configuration/source compatibility with the P0 coordinator.
    /// P1 no longer consumes a one-shot focus grace window: every valid native
    /// focus event is an independent last-event-wins re-apply request.
    /// </summary>
    public TimeSpan EntryFocusGracePeriod { get; init; } =
        TimeSpan.FromMilliseconds(1000);

    public int MaxAttempts { get; init; } = 3;

    internal TimeSpan GetRetryDelay(int completedAttempt) =>
        completedAttempt <= 1 ? RetryDelay : FinalRetryDelay;

    internal void Validate()
    {
        if (ForegroundDebounce < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ForegroundDebounce),
                "Event debounce cannot be negative.");
        }

        if (RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryDelay),
                "Retry delay cannot be negative.");
        }

        if (FinalRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FinalRetryDelay),
                "Final retry delay cannot be negative.");
        }

        if (EntryFocusGracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EntryFocusGracePeriod),
                "Entry focus grace period cannot be negative.");
        }

        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAttempts),
                "At least one apply attempt is required.");
        }
    }
}
