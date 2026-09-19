namespace FlowIME.Core.Settings;

public enum InputStatusOverlayPosition
{
    Caret,
    TopLeft,
    TopCenter,
    TopRight,
    Center,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public enum InputStatusOverlaySize
{
    Small,
    Medium,
    Large
}

/// <summary>
/// Controls the lightweight, non-activating input-state overlay.
/// Defaults preserve the original bottom-center presentation while allowing
/// users to move and tune the indicator for their own desktop layout.
/// </summary>
public sealed record InputStatusOverlaySettings
{
    public bool Enabled { get; init; } = true;

    public InputStatusOverlayPosition Position { get; init; } =
        InputStatusOverlayPosition.BottomCenter;

    public InputStatusOverlaySize Size { get; init; } = InputStatusOverlaySize.Medium;

    public int OpacityPercent { get; init; } = 92;

    public bool AnimationsEnabled { get; init; } = true;

    public InputStatusOverlaySettings Normalize() =>
        this with
        {
            Position = Enum.IsDefined(Position)
                ? Position
                : InputStatusOverlayPosition.BottomCenter,
            Size = Enum.IsDefined(Size) ? Size : InputStatusOverlaySize.Medium,
            OpacityPercent = Math.Clamp(OpacityPercent, 40, 100)
        };

    public static InputStatusOverlaySettings Default { get; } = new();
}
