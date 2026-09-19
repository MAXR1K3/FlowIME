namespace FlowIME.Core.Models;

public sealed class InputFocusChangedEventArgs : EventArgs
{
    public InputFocusChangedEventArgs(
        nint hwnd,
        int objectId,
        int childId,
        DateTimeOffset timestamp)
    {
        Hwnd = hwnd;
        ObjectId = objectId;
        ChildId = childId;
        Timestamp = timestamp;
    }

    public nint Hwnd { get; }

    public int ObjectId { get; }

    public int ChildId { get; }

    public DateTimeOffset Timestamp { get; }
}
