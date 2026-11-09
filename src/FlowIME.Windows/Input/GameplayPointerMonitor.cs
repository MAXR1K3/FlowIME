using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Input;

public enum CursorShape
{
    Unknown,
    Arrow,
    TextBeam,
    Hand,
    Busy,
    Custom
}

public enum CursorClip
{
    None,
    Window,
    Partial
}

/// <summary>
/// One content-free reading of the system pointer while a game is in front:
/// whether it is shown, its shape, whether it is confined to the game, and
/// whether the game thread reports a caret or mouse capture.
/// </summary>
public sealed record CursorObservation(
    bool Showing,
    bool Suppressed,
    CursorShape Shape,
    CursorClip Clip,
    bool InsideWindow,
    bool HasCaret,
    bool HasCapture)
{
    internal string Describe() =>
        $"showing={Showing} suppressed={Suppressed} shape={Shape} clip={Clip} " +
        $"inside={InsideWindow} caret={HasCaret} capture={HasCapture}";
}

internal interface ICursorStateNativeApi
{
    CursorObservation? Capture(nint hwnd, uint threadId);
}

/// <summary>
/// Watches the system pointer while a game is in front. Each reading is raised
/// through <see cref="Sampled"/> for pointer-based menu mode, and a log line is
/// written only when the reading changes, so real games can be diagnosed. It
/// never reads typed text or window contents.
/// </summary>
public sealed class GameplayPointerMonitor : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _sync = new();
    private readonly ICursorStateNativeApi _native;
    private readonly Action<string> _log;
    private readonly Timer _timer;
    private readonly TimeSpan _interval;
    private nint _hwnd;
    private uint _threadId;
    private string _processName = "unknown";
    private bool _active;
    private bool _disposed;
    private CursorObservation? _last;

    /// <summary>Raised on a background thread with the game window and its reading.</summary>
    public event Action<nint, CursorObservation>? Sampled;

    public GameplayPointerMonitor()
        : this(new Win32CursorStateNativeApi(), static line => Trace.WriteLine(line), DefaultInterval)
    {
    }

    internal GameplayPointerMonitor(ICursorStateNativeApi native, Action<string> log, TimeSpan interval)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _interval = interval;
        _timer = new Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void UpdateContext(bool gameContext, nint hwnd, uint threadId, string? processName)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var target = gameContext && hwnd != 0;
            if (target && _active && _hwnd == hwnd)
            {
                return;
            }

            if (_active)
            {
                _log($"[FlowIME.CursorProbe] utc={DateTimeOffset.UtcNow:O} stage=stop process={_processName}");
            }

            _active = target;
            _last = null;
            _hwnd = target ? hwnd : 0;
            _threadId = target ? threadId : 0;
            _processName = string.IsNullOrWhiteSpace(processName) ? "unknown" : processName;
            // The first reading waits one interval, after the game window has settled.
            _timer.Change(target ? _interval : Timeout.InfiniteTimeSpan, target ? _interval : Timeout.InfiniteTimeSpan);
            if (target)
            {
                _log($"[FlowIME.CursorProbe] utc={DateTimeOffset.UtcNow:O} stage=start process={_processName}");
            }
        }
    }

    internal void Sample()
    {
        nint hwnd;
        uint threadId;
        lock (_sync)
        {
            if (!_active || _disposed)
            {
                return;
            }

            hwnd = _hwnd;
            threadId = _threadId;
        }

        CursorObservation? observation;
        try
        {
            observation = _native.Capture(hwnd, threadId);
        }
        catch (Exception ex) when (ex is ExternalException or EntryPointNotFoundException)
        {
            observation = null;
        }

        if (observation is null)
        {
            return;
        }

        lock (_sync)
        {
            if (!_active || _hwnd != hwnd)
            {
                return;
            }

            if (observation != _last)
            {
                _last = observation;
                _log($"[FlowIME.CursorProbe] utc={DateTimeOffset.UtcNow:O} stage=change process={_processName} {observation.Describe()}");
            }
        }

        Sampled?.Invoke(hwnd, observation);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _active = false;
        }

        _timer.Dispose();
    }

    internal static CursorClip ClassifyClip(User32Native.Rect clip, User32Native.Rect window, User32Native.Rect screen)
    {
        if (clip.Left <= screen.Left && clip.Top <= screen.Top &&
            clip.Right >= screen.Right && clip.Bottom >= screen.Bottom)
        {
            return CursorClip.None;
        }

        // A few pixels of tolerance: games often clip to the client area.
        const int tolerance = 16;
        return Math.Abs(clip.Left - window.Left) <= tolerance &&
            Math.Abs(clip.Top - window.Top) <= tolerance * 3 &&
            Math.Abs(clip.Right - window.Right) <= tolerance &&
            Math.Abs(clip.Bottom - window.Bottom) <= tolerance
                ? CursorClip.Window
                : CursorClip.Partial;
    }
}

internal sealed class Win32CursorStateNativeApi : ICursorStateNativeApi
{
    private const uint CursorShowing = 0x00000001;
    private const uint CursorSuppressed = 0x00000002;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly nint Arrow = LoadCursorW(0, 32512);
    private static readonly nint TextBeam = LoadCursorW(0, 32513);
    private static readonly nint Wait = LoadCursorW(0, 32514);
    private static readonly nint AppStarting = LoadCursorW(0, 32650);
    private static readonly nint Hand = LoadCursorW(0, 32649);

    public CursorObservation? Capture(nint hwnd, uint threadId)
    {
        var info = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) ||
            !GetClipCursor(out var clip) ||
            !User32Native.GetWindowRect(hwnd, out var window))
        {
            return null;
        }

        var screen = new User32Native.Rect
        {
            Left = GetSystemMetrics(SmXVirtualScreen),
            Top = GetSystemMetrics(SmYVirtualScreen)
        };
        screen.Right = screen.Left + GetSystemMetrics(SmCxVirtualScreen);
        screen.Bottom = screen.Top + GetSystemMetrics(SmCyVirtualScreen);

        var gui = new User32Native.GuiThreadInfo { CbSize = (uint)Marshal.SizeOf<User32Native.GuiThreadInfo>() };
        var hasGui = threadId != 0 && User32Native.GetGUIThreadInfo(threadId, ref gui);

        return new CursorObservation(
            Showing: (info.Flags & CursorShowing) != 0,
            Suppressed: (info.Flags & CursorSuppressed) != 0,
            Shape: ClassifyShape(info.Cursor),
            Clip: GameplayPointerMonitor.ClassifyClip(clip, window, screen),
            InsideWindow: info.X >= window.Left && info.X < window.Right &&
                info.Y >= window.Top && info.Y < window.Bottom,
            HasCaret: hasGui && gui.HwndCaret != 0,
            HasCapture: hasGui && gui.HwndCapture != 0);
    }

    private static CursorShape ClassifyShape(nint cursor) =>
        cursor == 0 ? CursorShape.Unknown
        : cursor == Arrow ? CursorShape.Arrow
        : cursor == TextBeam ? CursorShape.TextBeam
        : cursor == Hand ? CursorShape.Hand
        : cursor == Wait || cursor == AppStarting ? CursorShape.Busy
        : CursorShape.Custom;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint Cursor;
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClipCursor(out User32Native.Rect rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint instance, nint cursorName);
}
