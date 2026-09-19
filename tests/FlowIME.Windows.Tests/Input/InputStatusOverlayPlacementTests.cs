using FlowIME.Core.Settings;
using FlowIME.Windows.Input;
using FlowIME.Windows.Interop;

namespace FlowIME.Windows.Tests.Input;

public sealed class InputStatusOverlayPlacementTests
{
    private static readonly User32Native.Rect WorkArea = new()
    {
        Left = 100,
        Top = 50,
        Right = 1100,
        Bottom = 850
    };

    [Theory]
    [InlineData(InputStatusOverlayPosition.TopLeft, 124, 74)]
    [InlineData(InputStatusOverlayPosition.TopCenter, 560, 74)]
    [InlineData(InputStatusOverlayPosition.TopRight, 996, 74)]
    [InlineData(InputStatusOverlayPosition.Center, 560, 422)]
    [InlineData(InputStatusOverlayPosition.BottomLeft, 124, 770)]
    [InlineData(InputStatusOverlayPosition.BottomCenter, 560, 770)]
    [InlineData(InputStatusOverlayPosition.BottomRight, 996, 770)]
    public void Screen_positions_respect_work_area_and_margin(
        InputStatusOverlayPosition position,
        int expectedX,
        int expectedY)
    {
        var point = InputStatusOverlayPlacement.Resolve(
            position,
            WorkArea,
            width: 80,
            height: 56,
            margin: 24,
            caretBounds: null,
            caretGap: 10);

        Assert.Equal(expectedX, point.X);
        Assert.Equal(expectedY, point.Y);
    }

    [Fact]
    public void Caret_position_appears_below_and_to_the_right_of_insertion_point()
    {
        var caret = new User32Native.Rect
        {
            Left = 500,
            Top = 300,
            Right = 502,
            Bottom = 324
        };

        var point = InputStatusOverlayPlacement.Resolve(
            InputStatusOverlayPosition.Caret,
            WorkArea,
            width: 80,
            height: 56,
            margin: 24,
            caretBounds: caret,
            caretGap: 10);

        Assert.Equal(512, point.X);
        Assert.Equal(334, point.Y);
    }

    [Fact]
    public void Caret_position_flips_above_and_clamps_inside_work_area()
    {
        var caret = new User32Native.Rect
        {
            Left = 1088,
            Top = 820,
            Right = 1090,
            Bottom = 844
        };

        var point = InputStatusOverlayPlacement.Resolve(
            InputStatusOverlayPosition.Caret,
            WorkArea,
            width: 80,
            height: 56,
            margin: 24,
            caretBounds: caret,
            caretGap: 10);

        Assert.Equal(996, point.X);
        Assert.Equal(754, point.Y);
    }

    [Fact]
    public void Missing_caret_falls_back_to_bottom_center()
    {
        var point = InputStatusOverlayPlacement.Resolve(
            InputStatusOverlayPosition.Caret,
            WorkArea,
            width: 80,
            height: 56,
            margin: 24,
            caretBounds: null,
            caretGap: 10);

        Assert.Equal(560, point.X);
        Assert.Equal(770, point.Y);
    }

    [Fact]
    public void Caret_bounds_fall_back_to_ui_automation_when_win32_does_not_expose_a_caret()
    {
        var expected = new User32Native.Rect
        {
            Left = 640,
            Top = 360,
            Right = 642,
            Bottom = 384
        };
        var resolver = new InputStatusCaretBoundsResolver(
            _ => null,
            _ => expected);

        var actual = resolver.Resolve(new nint(123));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Caret_bounds_prefer_win32_when_it_is_available()
    {
        var native = new User32Native.Rect
        {
            Left = 200,
            Top = 100,
            Right = 202,
            Bottom = 124
        };
        var automationCalls = 0;
        var resolver = new InputStatusCaretBoundsResolver(
            _ => native,
            _ =>
            {
                automationCalls++;
                return null;
            });

        var actual = resolver.Resolve(new nint(123));

        Assert.Equal(native, actual);
        Assert.Equal(0, automationCalls);
    }

    [Theory]
    [InlineData(false, 320)]
    [InlineData(true, 332)]
    public void Ui_automation_character_bounds_resolve_the_caret_edge(
        bool useRightEdge,
        int expectedX)
    {
        var bounds = UiAutomationCaretBoundsProvider.CreateCaretBounds(
            new System.Windows.Rect(320, 180, 12, 24),
            useRightEdge);

        Assert.Equal(expectedX, bounds.Left);
        Assert.Equal(expectedX + 1, bounds.Right);
        Assert.Equal(180, bounds.Top);
        Assert.Equal(204, bounds.Bottom);
    }
}
