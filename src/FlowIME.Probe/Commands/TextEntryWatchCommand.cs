using System.Runtime.InteropServices;
using System.Windows.Automation;
using FlowIME.Windows.Input;

namespace FlowIME.Probe.Commands;

/// <summary>
/// Diagnostic for text-entry detection. Prints, for the foreground window, which
/// signals change when the user moves between non-input areas and text boxes:
/// GUI thread focus/caret, UI Automation focused element, IMM open/conversion
/// state, and focus/caret WinEvents. Typed characters are never read.
/// </summary>
internal static class TextEntryWatchCommand
{
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectFocus = 0x8005;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjIdCaret = -8;

    internal static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Watching foreground text-entry signals. Press Ctrl+C to stop.");
        Console.WriteLine("Columns: process | focus | caret | uia | ime");

        var events = new WinEventPump();
        events.Start();
        try
        {
            var focusResolver = new FocusWindowResolver();
            var openStatus = new LegacyImeOpenStatusAccessor();
            var conversion = new LegacyImeConversionModeAccessor();
            string? previous = null;
            var uiaTick = 0;
            var uia = "-";

            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var line in events.Drain())
                {
                    Console.WriteLine($"{Now()}   event {line}");
                }

                var foreground = GetForegroundWindow();
                if (foreground != 0)
                {
                    if (uiaTick++ % 2 == 0)
                    {
                        uia = DescribeUiaFocus();
                    }

                    var line = Describe(foreground, focusResolver, openStatus, conversion, uia);
                    if (line != previous)
                    {
                        Console.WriteLine($"{Now()} {line}");
                        previous = line;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            events.Dispose();
        }

        return ProbeExitCode.Success;
    }

    private static string Now() => DateTimeOffset.Now.ToString("HH:mm:ss.fff");

    private static string Describe(
        nint foreground,
        FocusWindowResolver focusResolver,
        LegacyImeOpenStatusAccessor openStatus,
        LegacyImeConversionModeAccessor conversion,
        string uia)
    {
        var threadId = GetWindowThreadProcessId(foreground, out var processId);
        var process = ProcessName(processId);
        var info = new GuiThreadInfo { CbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        var hasInfo = GetGUIThreadInfo(threadId, ref info);

        var focus = !hasInfo
            ? "unavailable"
            : info.HwndFocus == 0
                ? "none"
                : info.HwndFocus == foreground
                    ? $"top({ClassName(info.HwndFocus)})"
                    : $"child({ClassName(info.HwndFocus)})";
        var caret = !hasInfo || info.HwndCaret == 0
            ? "none"
            : $"{ClassName(info.HwndCaret)} blink={(info.Flags & 0x1) != 0} " +
              $"rect={info.RcCaret.Left},{info.RcCaret.Top},{info.RcCaret.Right - info.RcCaret.Left}x{info.RcCaret.Bottom - info.RcCaret.Top}";

        var target = focusResolver.Resolve(foreground).EffectiveInputWindow;
        var open = openStatus.GetOpenStatus(target);
        var mode = conversion.GetConversionMode(target);
        var ime = $"open={(open.Success ? open.IsOpen.ToString() : "fail")} " +
                  $"conv={(mode.Success ? $"0x{mode.ConversionMode:X}" : "fail")}";

        return $"{process} | focus={focus} | caret={caret} | uia={uia} | {ime}";
    }

    private static string DescribeUiaFocus()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null)
            {
                return "none";
            }

            var current = element.Current;
            var readOnly = element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) &&
                value is ValuePattern valuePattern
                    ? valuePattern.Current.IsReadOnly.ToString()
                    : "n/a";
            var text = element.TryGetCurrentPattern(TextPattern.Pattern, out _);
            return $"{current.ControlType.ProgrammaticName.Replace("ControlType.", string.Empty)}" +
                   $"/class={current.ClassName}/readOnly={readOnly}/text={text}";
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return $"pid{processId}";
        }
        catch (InvalidOperationException)
        {
            return $"pid{processId}";
        }
    }

    private static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var count = GetClassNameW(hwnd, buffer, buffer.Length);
        return count > 0 ? new string(buffer, 0, count) : "?";
    }

    /// <summary>
    /// Out-of-context WinEvent hook for focus and caret events of the foreground
    /// process. Location changes are counted, not printed one by one.
    /// </summary>
    private sealed class WinEventPump : IDisposable
    {
        private readonly object _sync = new();
        private readonly List<string> _pending = [];
        private readonly Dictionary<string, int> _locationCounts = [];
        private readonly WinEventProc _callback;
        private readonly Thread _thread;
        private uint _threadId;

        public WinEventPump()
        {
            _callback = OnEvent;
            _thread = new Thread(Loop) { IsBackground = true, Name = "TextEntryWatch.WinEvents" };
        }

        public void Start() => _thread.Start();

        public IReadOnlyList<string> Drain()
        {
            lock (_sync)
            {
                foreach (var (key, count) in _locationCounts)
                {
                    _pending.Add($"caret-location x{count} {key}");
                }

                _locationCounts.Clear();
                var drained = _pending.ToArray();
                _pending.Clear();
                return drained;
            }
        }

        public void Dispose()
        {
            if (_threadId != 0)
            {
                _ = PostThreadMessageW(_threadId, 0x0012, 0, 0);
            }
        }

        private void Loop()
        {
            _threadId = GetCurrentThreadId();
            var hook = SetWinEventHook(EventObjectShow, EventObjectLocationChange, 0, _callback, 0, 0, 0x0002);
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                _ = TranslateMessage(in message);
                _ = DispatchMessageW(in message);
            }

            _ = UnhookWinEvent(hook);
        }

        private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == 0)
            {
                return;
            }

            var isCaret = idObject == ObjIdCaret &&
                eventType is EventObjectShow or EventObjectHide or EventObjectLocationChange;
            if (!isCaret && eventType != EventObjectFocus)
            {
                return;
            }

            var foreground = GetForegroundWindow();
            _ = GetWindowThreadProcessId(foreground, out var foregroundPid);
            _ = GetWindowThreadProcessId(hwnd, out var eventPid);
            if (foregroundPid == 0 || foregroundPid != eventPid)
            {
                return;
            }

            var key = $"hwnd={ClassName(hwnd)}{(hwnd == foreground ? "(top)" : string.Empty)} obj={idObject} child={idChild}";
            lock (_sync)
            {
                if (eventType == EventObjectLocationChange)
                {
                    _locationCounts[key] = _locationCounts.GetValueOrDefault(key) + 1;
                    return;
                }

                var name = eventType switch
                {
                    EventObjectShow => "caret-show",
                    EventObjectHide => "caret-hide",
                    _ => "focus"
                };
                _pending.Add($"{name} {key}");
            }
        }
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint CbSize;
        public uint Flags;
        public nint HwndActive;
        public nint HwndFocus;
        public nint HwndCapture;
        public nint HwndMenuOwner;
        public nint HwndMoveSize;
        public nint HwndCaret;
        public Rect RcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg message, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(in Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(in Msg message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
