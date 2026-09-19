using System.Runtime.InteropServices;

namespace FlowIME.Windows.Interop;

internal enum QueryUserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningDirect3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7
}

internal static class Shell32Native
{
    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(
        out QueryUserNotificationState state);
}
