namespace FlowIME.App.Services;

public sealed record RecentGameplayTarget(
    string ApplicationIdentityKey,
    string ProcessName,
    DateTimeOffset SeenAt,
    string? ExecutablePath = null);
