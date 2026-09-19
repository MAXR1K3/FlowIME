namespace FlowIME.Core.Context;

/// <summary>
/// Parser/formatter for the small, human-editable gesture language used by per-game
/// chat profiles. It intentionally supports ordinary keyboard keys only; mouse and
/// scan-code specific bindings belong to future game adapters.
/// </summary>
public static class GameTextEntryKeyGestureParser
{
    private static readonly IReadOnlyDictionary<string, uint> NamedKeys =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = 0x0D,
            ["Return"] = 0x0D,
            ["Esc"] = 0x1B,
            ["Escape"] = 0x1B,
            ["Space"] = 0x20,
            ["Tab"] = 0x09,
            ["Backspace"] = 0x08,
            ["Delete"] = 0x2E,
            ["Insert"] = 0x2D,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27,
            ["Slash"] = 0xBF,
            ["/"] = 0xBF,
            ["Backslash"] = 0xDC,
            ["\\"] = 0xDC,
            ["Semicolon"] = 0xBA,
            ["Comma"] = 0xBC,
            ["Period"] = 0xBE,
            ["Quote"] = 0xDE,
            ["Minus"] = 0xBD,
            ["Equals"] = 0xBB,
            ["BracketLeft"] = 0xDB,
            ["BracketRight"] = 0xDD,
            ["Grave"] = 0xC0
        };

    public static IReadOnlyList<GameTextEntryKeyGesture> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<GameTextEntryKeyGesture>();
        }

        var result = new List<GameTextEntryKeyGesture>();
        foreach (var token in text.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var gesture = Parse(token);
            if (!result.Contains(gesture))
            {
                result.Add(gesture);
            }
        }

        return result;
    }

    public static GameTextEntryKeyGesture Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Gesture cannot be empty.");
        }

        var modifiers = GameTextEntryModifierKeys.None;
        uint? virtualKey = null;
        foreach (var rawPart in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= GameTextEntryModifierKeys.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= GameTextEntryModifierKeys.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= GameTextEntryModifierKeys.Shift;
                continue;
            }

            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= GameTextEntryModifierKeys.Windows;
                continue;
            }

            if (virtualKey.HasValue)
            {
                throw new FormatException($"Gesture '{text}' contains more than one non-modifier key.");
            }

            virtualKey = ParseVirtualKey(part);
        }

        if (!virtualKey.HasValue)
        {
            throw new FormatException($"Gesture '{text}' does not contain a key.");
        }

        return new GameTextEntryKeyGesture(virtualKey.Value, modifiers);
    }

    public static string FormatList(IEnumerable<GameTextEntryKeyGesture>? gestures) =>
        string.Join(", ", (gestures ?? Array.Empty<GameTextEntryKeyGesture>()).Select(Format));

    public static string Format(GameTextEntryKeyGesture gesture)
    {
        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(GameTextEntryModifierKeys.Control)) parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(GameTextEntryModifierKeys.Alt)) parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(GameTextEntryModifierKeys.Shift)) parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(GameTextEntryModifierKeys.Windows)) parts.Add("Win");
        parts.Add(FormatVirtualKey(gesture.VirtualKey));
        return string.Join("+", parts);
    }

    private static uint ParseVirtualKey(string token)
    {
        if (NamedKeys.TryGetValue(token, out var named))
        {
            return named;
        }

        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z' || character is >= '0' and <= '9')
            {
                return character;
            }
        }

        if (token.Length is >= 2 and <= 3 &&
            token[0] is 'F' or 'f' &&
            int.TryParse(token.AsSpan(1), out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return checked((uint)(0x70 + functionKey - 1));
        }

        throw new FormatException($"Unsupported key '{token}'.");
    }

    private static string FormatVirtualKey(uint virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A || virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        var preferred = NamedKeys
            .Where(pair => pair.Value == virtualKey)
            .Select(pair => pair.Key)
            .FirstOrDefault(key => key is not "Return" and not "Escape" and not "/" and not "\\");
        return preferred ?? $"VK_{virtualKey:X2}";
    }
}
