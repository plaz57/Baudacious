using System.Text;

namespace Baudacious.Core.Config;

/// <summary>What a line of an INI file turned out to be.</summary>
public enum IniLineKind
{
    Blank,
    Comment,
    Section,
    Entry,

    /// <summary>Something that is none of the above. Reported, then ignored.</summary>
    Malformed,
}

/// <summary>A complaint about one line, with somewhere to point.</summary>
/// <param name="Line">One-based, so it matches what an editor shows.</param>
public readonly record struct IniProblem(int Line, string Message)
{
    public override string ToString() => $"line {Line}: {Message}";
}

/// <summary>
/// An INI file that remembers exactly how it was written.
/// </summary>
/// <remarks>
/// The point of this class is the round trip. The configuration file is the user's — their
/// comments, their spacing, their ordering — and the application has to be able to change a
/// value in it without rewriting the rest. So every line keeps its original text, and only
/// a line actually assigned to is rebuilt.
/// <para>
/// <see cref="ToText"/> after a <see cref="Parse"/> with no edits returns the input byte for
/// byte, trailing newline or not. That is a property the tests assert directly, because
/// everything else here depends on it.
/// </para>
/// </remarks>
public sealed class IniDocument
{
    private readonly List<Line> _lines = [];
    private readonly List<IniProblem> _problems = [];
    private readonly string _newline;
    private readonly bool _endsWithNewline;

    private IniDocument(string newline, bool endsWithNewline)
    {
        _newline = newline;
        _endsWithNewline = endsWithNewline;
    }

    /// <summary>Everything wrong with the file, in the order it was found.</summary>
    public IReadOnlyList<IniProblem> Problems => _problems;

    /// <summary>An empty document that will use the platform's line ending.</summary>
    public static IniDocument Empty() => new(Environment.NewLine, endsWithNewline: true);

    public static IniDocument Parse(string? text)
    {
        text ??= string.Empty;

        // Whatever the file already uses, keep using. A file edited on one platform and
        // read on another should not have its line endings quietly converted.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = text.Length == 0 || text.EndsWith('\n');

        var document = new IniDocument(newline, endsWithNewline);
        var section = string.Empty;
        var number = 0;

        foreach (var raw in Split(text))
        {
            number++;
            var line = Classify(raw, number, section, document._problems);

            if (line.Kind == IniLineKind.Section)
            {
                section = line.Section;
            }

            if (line.Kind == IniLineKind.Entry
                && document.Find(line.Section, line.Key) is not null)
            {
                document._problems.Add(new IniProblem(
                    number, $"'{line.Key}' is set more than once in [{line.Section}]; the last wins"));
            }

            document._lines.Add(line);
        }

        return document;
    }

    /// <summary>The value for a key, or null when it is not set.</summary>
    /// <remarks>
    /// The last assignment wins, which is what makes copying a line to edit it behave the
    /// way people expect.
    /// </remarks>
    public string? Get(string section, string key)
    {
        string? found = null;
        foreach (var line in _lines)
        {
            if (line.Kind == IniLineKind.Entry
                && line.Section.Equals(section, StringComparison.OrdinalIgnoreCase)
                && line.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                found = line.Value;
            }
        }

        return found;
    }

    /// <summary>The one-based line a key is set on, for pointing at a bad value.</summary>
    public int LineOf(string section, string key) => Find(section, key)?.Number ?? 0;

    /// <summary>Every section named in the file, in order, without duplicates.</summary>
    public IReadOnlyList<string> Sections()
    {
        var names = new List<string>();
        foreach (var line in _lines)
        {
            if (line.Kind == IniLineKind.Section
                && !names.Contains(line.Section, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(line.Section);
            }
        }

        return names;
    }

    /// <summary>The keys set in a section, in order.</summary>
    public IReadOnlyList<string> Keys(string section)
    {
        var keys = new List<string>();
        foreach (var line in _lines)
        {
            if (line.Kind == IniLineKind.Entry
                && line.Section.Equals(section, StringComparison.OrdinalIgnoreCase)
                && !keys.Contains(line.Key, StringComparer.OrdinalIgnoreCase))
            {
                keys.Add(line.Key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Sets a value, in place where the key already exists.
    /// </summary>
    /// <remarks>
    /// In place means the surrounding text survives: the indentation, the spacing around
    /// the equals sign, and any comment after the value are all kept, and only the value
    /// itself changes.
    /// </remarks>
    public void Set(string section, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (Find(section, key) is { } existing)
        {
            existing.Value = value;
            existing.Raw = null;   // rebuild this one line, and only this one
            return;
        }

        var insertAt = LastLineOf(section);
        if (insertAt < 0)
        {
            // No such section yet. Start one at the end, with a blank line before it unless
            // the file is empty or already ends in one.
            if (_lines.Count > 0 && _lines[^1].Kind != IniLineKind.Blank)
            {
                _lines.Add(new Line { Kind = IniLineKind.Blank, Raw = string.Empty });
            }

            _lines.Add(new Line
            {
                Kind = IniLineKind.Section,
                Section = section,
                Raw = $"[{section}]",
            });

            insertAt = _lines.Count - 1;
        }

        _lines.Insert(insertAt + 1, new Line
        {
            Kind = IniLineKind.Entry,
            Section = section,
            Key = key,
            Value = value,
            Separator = " = ",
        });

        Renumber();
    }

    /// <summary>Appends a comment line to a section, or to the end of the file.</summary>
    public void Comment(string section, string text)
    {
        var at = LastLineOf(section);
        var line = new Line { Kind = IniLineKind.Comment, Section = section, Raw = "; " + text };

        if (at < 0)
        {
            _lines.Add(line);
        }
        else
        {
            _lines.Insert(at + 1, line);
        }

        Renumber();
    }

    public override string ToString() => ToText();

    public string ToText()
    {
        var builder = new StringBuilder();

        for (var i = 0; i < _lines.Count; i++)
        {
            builder.Append(_lines[i].Text());

            if (i < _lines.Count - 1 || _endsWithNewline)
            {
                builder.Append(_newline);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits into lines without discarding anything.
    /// </summary>
    /// <remarks>
    /// A file ending in a newline is one line shorter than the split suggests: "a\n" is one
    /// line, not two, and writing it back must not add a blank one.
    /// </remarks>
    private static IEnumerable<string> Split(string text)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        var lines = text.Split('\n');
        var count = text.EndsWith('\n') ? lines.Length - 1 : lines.Length;

        for (var i = 0; i < count; i++)
        {
            yield return lines[i].TrimEnd('\r');
        }
    }

    private static Line Classify(string raw, int number, string section, List<IniProblem> problems)
    {
        var body = raw.TrimStart();
        var indent = raw[..(raw.Length - body.Length)];

        if (body.Length == 0)
        {
            return new Line { Number = number, Kind = IniLineKind.Blank, Section = section, Raw = raw };
        }

        if (body[0] is ';' or '#')
        {
            return new Line { Number = number, Kind = IniLineKind.Comment, Section = section, Raw = raw };
        }

        if (body[0] == '[')
        {
            var close = body.IndexOf(']', StringComparison.Ordinal);
            var name = close < 0 ? string.Empty : body[1..close].Trim();

            if (close < 0 || name.Length == 0)
            {
                problems.Add(new IniProblem(number, "a section header must read [name]"));
                return new Line { Number = number, Kind = IniLineKind.Malformed, Section = section, Raw = raw };
            }

            return new Line
            {
                Number = number,
                Kind = IniLineKind.Section,
                Section = name,
                Raw = raw,
            };
        }

        var equals = body.IndexOf('=', StringComparison.Ordinal);
        if (equals < 0)
        {
            problems.Add(new IniProblem(number, $"expected 'key = value', found '{Shorten(body)}'"));
            return new Line { Number = number, Kind = IniLineKind.Malformed, Section = section, Raw = raw };
        }

        var key = body[..equals].TrimEnd();
        if (key.Length == 0)
        {
            problems.Add(new IniProblem(number, "a setting needs a name before the '='"));
            return new Line { Number = number, Kind = IniLineKind.Malformed, Section = section, Raw = raw };
        }

        var after = body[(equals + 1)..];
        var (value, trailing) = SplitTrailingComment(after);

        return new Line
        {
            Number = number,
            Kind = IniLineKind.Entry,
            Section = section,
            Key = key,
            Value = value,
            Indent = indent,
            Separator = body[key.Length..(equals + 1)] + LeadingSpace(after),
            Trailing = trailing,
            Raw = raw,
        };
    }

    /// <summary>
    /// Separates a value from a comment after it.
    /// </summary>
    /// <remarks>
    /// A ';' or '#' only starts a comment when whitespace comes before it, so a value is
    /// free to contain either character. None of the settings do, but a shell profile's
    /// command line easily could.
    /// </remarks>
    private static (string Value, string Trailing) SplitTrailingComment(string after)
    {
        var trimmed = after.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is ';' or '#')
        {
            return (string.Empty, after[(after.Length - trimmed.Length)..]);
        }

        for (var i = 1; i < after.Length; i++)
        {
            if (after[i] is ';' or '#' && char.IsWhiteSpace(after[i - 1]))
            {
                // The whitespace belongs to the comment, not to the value.
                var start = i;
                while (start > 0 && char.IsWhiteSpace(after[start - 1]))
                {
                    start--;
                }

                return (after[..start].Trim(), after[start..]);
            }
        }

        return (after.Trim(), string.Empty);
    }

    private static string LeadingSpace(string after)
    {
        var i = 0;
        while (i < after.Length && (after[i] == ' ' || after[i] == '\t'))
        {
            i++;
        }

        return after[..i];
    }

    private static string Shorten(string text) => text.Length <= 30 ? text : text[..27] + "...";

    private Line? Find(string section, string key)
    {
        Line? found = null;
        foreach (var line in _lines)
        {
            if (line.Kind == IniLineKind.Entry
                && line.Section.Equals(section, StringComparison.OrdinalIgnoreCase)
                && line.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                found = line;
            }
        }

        return found;
    }

    /// <summary>
    /// The index of the last line belonging to a section, or -1 if it has none.
    /// </summary>
    /// <remarks>
    /// Blank lines at the end of a section are skipped, so a value added to it lands under
    /// the section's own text rather than after the gap that separates it from the next.
    /// Every line carries the section in force when it was read, the header included, so
    /// one comparison answers for all of them.
    /// </remarks>
    private int LastLineOf(string section)
    {
        var last = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (!line.Section.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Kind != IniLineKind.Blank)
            {
                last = i;
            }
            else if (last < 0)
            {
                // A blank before anything else still marks the section as present.
                last = i;
            }
        }

        return last;
    }

    private void Renumber()
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].Number = i + 1;
        }
    }

    private sealed class Line
    {
        public int Number { get; set; }

        public IniLineKind Kind { get; init; }

        public string Section { get; init; } = string.Empty;

        public string Key { get; init; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string Indent { get; init; } = string.Empty;

        /// <summary>Whatever sat between the key and the value, '=' included.</summary>
        public string Separator { get; init; } = " = ";

        /// <summary>A comment after the value, with the whitespace that introduced it.</summary>
        public string Trailing { get; init; } = string.Empty;

        /// <summary>The line as written, or null once it has been assigned to.</summary>
        public string? Raw { get; set; }

        public string Text() => Raw ?? (Indent + Key + Separator + Value + Trailing);
    }
}
