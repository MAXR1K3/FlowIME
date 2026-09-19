namespace FlowIME.Core.Models;

public sealed record InputOperationResult(
    bool Success,
    InputState Before,
    InputState After,
    string Backend,
    string? ErrorCode,
    TimeSpan Duration);
