namespace FlowIME.Core.Models;

public sealed class ForegroundWindowChangedEventArgs : EventArgs
{
    public ForegroundWindowChangedEventArgs(nint hwnd, DateTimeOffset timestamp)
    {
        Hwnd = hwnd;
        Timestamp = timestamp;
    }

    public nint Hwnd { get; }

    public DateTimeOffset Timestamp { get; }
}
