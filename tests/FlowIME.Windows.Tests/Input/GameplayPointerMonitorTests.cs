using FlowIME.Windows.Input;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Tests.Input;

public sealed class GameplayPointerMonitorTests
{
    private static readonly CursorObservation Menu = new(
        Showing: true, Suppressed: false, CursorShape.Arrow, CursorClip.None,
        InsideWindow: true, HasCaret: false, HasCapture: false);

    private static readonly CursorObservation Match = Menu with
    {
        Showing = false,
        Clip = CursorClip.Window
    };

    [Fact]
    public void Logs_only_when_the_pointer_state_changes()
    {
        var native = new FakeNative { Next = Menu };
        var lines = new List<string>();
        using var monitor = new GameplayPointerMonitor(native, lines.Add, Timeout.InfiniteTimeSpan);

        monitor.UpdateContext(gameContext: true, hwnd: 0x10, threadId: 7, processName: "cod");
        monitor.Sample();
        monitor.Sample();
        native.Next = Match;
        monitor.Sample();

        var changes = lines.Where(static line => line.Contains("stage=change", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, changes.Length);
        Assert.Contains("showing=True", changes[0], StringComparison.Ordinal);
        Assert.Contains("showing=False", changes[1], StringComparison.Ordinal);
        Assert.Contains("clip=Window", changes[1], StringComparison.Ordinal);
        Assert.All(lines, static line => Assert.Contains("process=cod", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_reading_is_raised_for_menu_mode()
    {
        var native = new FakeNative { Next = Menu };
        var sampled = new List<(nint Hwnd, CursorObservation Observation)>();
        using var monitor = new GameplayPointerMonitor(native, static _ => { }, Timeout.InfiniteTimeSpan);
        monitor.Sampled += (hwnd, observation) => sampled.Add((hwnd, observation));

        monitor.UpdateContext(gameContext: true, hwnd: 0x10, threadId: 7, processName: "cod");
        monitor.Sample();
        monitor.Sample();

        Assert.Equal([(0x10, Menu), (0x10, Menu)], sampled);
    }

    [Fact]
    public void Samples_only_while_a_game_is_in_front()
    {
        var native = new FakeNative { Next = Menu };
        var lines = new List<string>();
        using var monitor = new GameplayPointerMonitor(native, lines.Add, Timeout.InfiniteTimeSpan);

        monitor.Sample();
        Assert.Empty(lines);

        monitor.UpdateContext(gameContext: true, hwnd: 0x10, threadId: 7, processName: "cod");
        monitor.UpdateContext(gameContext: false, hwnd: 0x20, threadId: 8, processName: "chrome");
        monitor.Sample();

        Assert.Equal(["stage=start", "stage=stop"], lines.Select(static line => line.Split(' ')[2]));
        Assert.Equal(0, native.Captures);
    }

    [Theory]
    [InlineData(0, 0, 2560, 1440, "None")]
    [InlineData(0, 0, 2560, 1400, "Window")]
    [InlineData(500, 500, 600, 600, "Partial")]
    public void Clip_is_read_against_the_screen_and_the_game_window(
        int left, int top, int right, int bottom, string expected)
    {
        var screen = new User32Native.Rect { Left = 0, Top = 0, Right = 2560, Bottom = 1440 };
        var window = new User32Native.Rect { Left = 0, Top = 0, Right = 2560, Bottom = 1400 };
        var clip = new User32Native.Rect { Left = left, Top = top, Right = right, Bottom = bottom };

        Assert.Equal(Enum.Parse<CursorClip>(expected), GameplayPointerMonitor.ClassifyClip(clip, window, screen));
    }

    private sealed class FakeNative : ICursorStateNativeApi
    {
        internal CursorObservation? Next { get; set; }

        internal int Captures { get; private set; }

        public CursorObservation? Capture(nint hwnd, uint threadId)
        {
            Captures++;
            return Next;
        }
    }
}
