using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Silk.NET.Input;

namespace Baudacious.App.Config;

/// <summary>
/// A key with its modifiers, as a configuration file spells it: <c>alt+B</c>,
/// <c>alt+shift+F</c>, <c>ctrl+]</c>.
/// </summary>
/// <remarks>
/// Shift is part of the chord rather than a way of naming a different key, so
/// <c>alt+shift+F</c> and <c>alt+F</c> are two bindings and not one. Modifier order does
/// not matter and case does not either.
/// </remarks>
public readonly record struct KeyChord(Key Key, bool Alt, bool Control, bool Shift)
{
    /// <summary>The chord that is no chord. Matches nothing.</summary>
    public static KeyChord None => new(Key.Unknown, false, false, false);

    public bool IsNone => Key == Key.Unknown;

    /// <summary>Whether a key press with these modifiers is this chord.</summary>
    public bool Matches(Key key, bool alt, bool control, bool shift) =>
        !IsNone && key == Key && alt == Alt && control == Control && shift == Shift;

    public static bool TryParse(string? text, [NotNullWhen(true)] out KeyChord chord)
    {
        chord = None;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // "ctrl++" would split to nothing but the modifiers, so the plus key is spelled
        // "plus" and this stays a simple split.
        if (parts.Length == 0)
        {
            return false;
        }

        var alt = false;
        var control = false;
        var shift = false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "alt": alt = true; break;
                case "ctrl" or "control": control = true; break;
                case "shift": shift = true; break;
                default: return false;
            }
        }

        if (!TryParseKey(parts[^1], out var key))
        {
            return false;
        }

        chord = new KeyChord(key, alt, control, shift);
        return true;
    }

    public override string ToString()
    {
        if (IsNone)
        {
            return "none";
        }

        var name = NameOf(Key);
        return (Alt ? "alt+" : string.Empty)
               + (Control ? "ctrl+" : string.Empty)
               + (Shift ? "shift+" : string.Empty)
               + name;
    }

    /// <summary>
    /// The keys a binding may name, beyond the letters and digits.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Every entry is a key that exists on an ordinary keyboard and
    /// that the window can actually receive; there is no point offering names for keys
    /// GLFW will never report.
    /// </remarks>
    private static readonly (string Name, Key Key)[] Named =
    [
        ("minus", Key.Minus),
        ("equals", Key.Equal),
        ("plus", Key.Equal),
        ("[", Key.LeftBracket),
        ("]", Key.RightBracket),
        ("leftbracket", Key.LeftBracket),
        ("rightbracket", Key.RightBracket),
        ("backslash", Key.BackSlash),
        ("semicolon", Key.Semicolon),
        ("apostrophe", Key.Apostrophe),
        ("comma", Key.Comma),
        ("period", Key.Period),
        ("slash", Key.Slash),
        ("grave", Key.GraveAccent),
        ("space", Key.Space),
        ("tab", Key.Tab),
        ("enter", Key.Enter),
        ("escape", Key.Escape),
        ("backspace", Key.Backspace),
        ("delete", Key.Delete),
        ("insert", Key.Insert),
        ("home", Key.Home),
        ("end", Key.End),
        ("pageup", Key.PageUp),
        ("pagedown", Key.PageDown),
        ("up", Key.Up),
        ("down", Key.Down),
        ("left", Key.Left),
        ("right", Key.Right),
    ];

    private static bool TryParseKey(string word, out Key key)
    {
        key = Key.Unknown;

        if (word.Length == 1)
        {
            var c = char.ToUpperInvariant(word[0]);

            if (c is >= 'A' and <= 'Z')
            {
                key = Key.A + (c - 'A');
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = Key.Number0 + (c - '0');
                return true;
            }
        }

        // F1 to F12.
        if (word.Length is 2 or 3
            && (word[0] is 'f' or 'F')
            && int.TryParse(word.AsSpan(1), CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 12)
        {
            key = Key.F1 + (number - 1);
            return true;
        }

        foreach (var (name, candidate) in Named)
        {
            if (name.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                return true;
            }
        }

        return false;
    }

    private static string NameOf(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return ((char)('A' + (key - Key.A))).ToString();
        }

        if (key is >= Key.Number0 and <= Key.Number9)
        {
            return ((char)('0' + (key - Key.Number0))).ToString();
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            return "F" + (key - Key.F1 + 1).ToString(CultureInfo.InvariantCulture);
        }

        foreach (var (name, candidate) in Named)
        {
            if (candidate == key)
            {
                return name;
            }
        }

        return key.ToString().ToLowerInvariant();
    }
}
