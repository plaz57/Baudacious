using Silk.NET.Input;

namespace Baudacious.App.Config;

/// <summary>Everything the local layer can be asked to do from the keyboard.</summary>
public enum LocalAction
{
    LineRate,
    Framing,
    FastForward,
    Skim,
    Phosphor,
    Crt,
    ScaleUp,
    ScaleDown,
    Fullscreen,
    NewSession,
    NextSession,
    Commands,
    Help,
    Quit,
}

/// <summary>
/// Which chord does what.
/// </summary>
/// <remarks>
/// A table rather than a field per action, so the configuration reader, the validator and
/// the window can all walk it without any of them holding a second list that could fall out
/// of step.
/// <para>
/// Shift is part of a chord, which is what lets alt+F and alt+shift+F be two bindings
/// instead of one binding with a special case inside it.
/// </para>
/// </remarks>
public sealed class KeyBindings
{
    /// <summary>The name each action goes by in the configuration file.</summary>
    private static readonly (LocalAction Action, string Name, string Chord, string Explains)[] Table =
    [
        (LocalAction.LineRate, "line-rate", "alt+B", "open the line rate menu"),
        (LocalAction.Framing, "framing", "alt+G", "open the framing menu"),
        (LocalAction.FastForward, "fast-forward", "alt+F", "release the queued backlog at once"),
        (LocalAction.Skim, "skim", "alt+shift+F", "ten times the rate while held"),
        (LocalAction.Phosphor, "phosphor", "alt+P", "open the palette menu"),
        (LocalAction.Crt, "crt", "alt+C", "turn the CRT pass on or off"),
        (LocalAction.ScaleUp, "scale-up", "alt+equals", "magnify the cell one step"),
        (LocalAction.ScaleDown, "scale-down", "alt+minus", "shrink the cell one step"),
        (LocalAction.Fullscreen, "fullscreen", "alt+enter", "fill the screen, or stop"),
        (LocalAction.NewSession, "new-session", "alt+N", "start another shell"),
        (LocalAction.NextSession, "next-session", "alt+S", "show the next shell"),
        (LocalAction.Commands, "commands", "ctrl+]", "open the local command prompt"),
        (LocalAction.Help, "help", "alt+Z", "show the key overlay"),
        (LocalAction.Quit, "quit", "alt+X", "close Baudacious; press twice"),
    ];

    private readonly Dictionary<LocalAction, KeyChord> _chords;

    private KeyBindings(Dictionary<LocalAction, KeyChord> chords) => _chords = chords;

    /// <summary>The bindings as shipped, which are also what the default config file says.</summary>
    public static KeyBindings Default { get; } = BuildDefault();

    /// <summary>Every action, in the order the configuration file lists them.</summary>
    public static IEnumerable<LocalAction> Actions => Table.Select(e => e.Action);

    public KeyChord this[LocalAction action] =>
        _chords.TryGetValue(action, out var chord) ? chord : KeyChord.None;

    /// <summary>The configuration key for an action, such as <c>line-rate</c>.</summary>
    public static string NameOf(LocalAction action) =>
        Table.First(e => e.Action == action).Name;

    /// <summary>A one-line explanation, for the generated config file's comments.</summary>
    public static string Explain(LocalAction action) =>
        Table.First(e => e.Action == action).Explains;

    /// <summary>The chord an action ships with, as written in the file.</summary>
    public static string DefaultChordOf(LocalAction action) =>
        Table.First(e => e.Action == action).Chord;

    public static bool TryParseAction(string? name, out LocalAction action)
    {
        foreach (var entry in Table)
        {
            if (entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                action = entry.Action;
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>
    /// What a key press is bound to, or null when it is not bound at all.
    /// </summary>
    /// <remarks>
    /// First match wins in table order. Two actions on one chord is a configuration error
    /// the reader reports; this is not the place to discover it.
    /// </remarks>
    public LocalAction? Find(Key key, bool alt, bool control, bool shift)
    {
        foreach (var entry in Table)
        {
            if (this[entry.Action].Matches(key, alt, control, shift))
            {
                return entry.Action;
            }
        }

        return null;
    }

    public KeyBindings With(LocalAction action, KeyChord chord)
    {
        var chords = new Dictionary<LocalAction, KeyChord>(_chords) { [action] = chord };
        return new KeyBindings(chords);
    }

    /// <summary>
    /// Two tables holding the same chords are the same bindings.
    /// </summary>
    /// <remarks>
    /// Value equality, not reference: <see cref="Settings"/> is a record, and a record
    /// compares its members with the default comparer. Without this, any settings object
    /// that had been through the reader would be unequal to an identical one, which is
    /// both surprising and exactly the sort of thing that only shows up in a test.
    /// </remarks>
    public override bool Equals(object? other)
    {
        if (other is not KeyBindings bindings)
        {
            return false;
        }

        foreach (var action in Actions)
        {
            if (this[action] != bindings[action])
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var action in Actions)
        {
            hash.Add(this[action]);
        }

        return hash.ToHashCode();
    }

    /// <summary>Actions sharing a chord, which cannot both work.</summary>
    public IEnumerable<(LocalAction First, LocalAction Second, KeyChord Chord)> Conflicts()
    {
        for (var i = 0; i < Table.Length; i++)
        {
            for (var j = i + 1; j < Table.Length; j++)
            {
                var a = this[Table[i].Action];
                var b = this[Table[j].Action];

                if (!a.IsNone && a == b)
                {
                    yield return (Table[i].Action, Table[j].Action, a);
                }
            }
        }
    }

    private static KeyBindings BuildDefault()
    {
        var chords = new Dictionary<LocalAction, KeyChord>();

        foreach (var entry in Table)
        {
            if (!KeyChord.TryParse(entry.Chord, out var chord))
            {
                throw new InvalidOperationException(
                    $"the built-in binding '{entry.Chord}' for {entry.Name} does not parse");
            }

            chords[entry.Action] = chord;
        }

        return new KeyBindings(chords);
    }
}
