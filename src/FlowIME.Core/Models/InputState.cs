namespace FlowIME.Core.Models;

public sealed record InputState(
    string? ProfileName,
    InputMode Mode,
    nint KeyboardLayout);
