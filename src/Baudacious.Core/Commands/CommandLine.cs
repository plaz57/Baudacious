using System.Text;

namespace Baudacious.Core.Commands;

/// <summary>
/// The editable line behind the local prompt: text, caret, history and completion.
/// </summary>
/// <remarks>
/// Deliberately not the shell's line editor and deliberately not metered — this is the
/// client talking to itself, so it answers at the speed of the keyboard however slow the
/// wire is. Nothing here reaches the shell.
/// </remarks>
public sealed class CommandLine
{
    /// <summary>How many past lines are kept. Long enough to find yesterday's rate change.</summary>
    public const int HistoryLimit = 100;

    private readonly List<string> _history = [];
    private readonly StringBuilder _text = new();

    /// <summary>Equal to <c>_history.Count</c> while the live line is being edited.</summary>
    private int _historyIndex;

    /// <summary>The live line, put aside while the history is being walked.</summary>
    private string _stashed = string.Empty;

    public string Text => _text.ToString();

    public int Caret { get; private set; }

    public bool IsEmpty => _text.Length == 0;

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<string> History => _history;

    public void Insert(char character)
    {
        _text.Insert(Caret, character);
        Caret++;
    }

    public void Insert(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text.Insert(Caret, text);
        Caret += text.Length;
    }

    public bool Backspace()
    {
        if (Caret == 0)
        {
            return false;
        }

        _text.Remove(Caret - 1, 1);
        Caret--;
        return true;
    }

    public bool Delete()
    {
        if (Caret >= _text.Length)
        {
            return false;
        }

        _text.Remove(Caret, 1);
        return true;
    }

    public void MoveLeft() => Caret = Math.Max(0, Caret - 1);

    public void MoveRight() => Caret = Math.Min(_text.Length, Caret + 1);

    public void MoveHome() => Caret = 0;

    public void MoveEnd() => Caret = _text.Length;

    /// <summary>Ctrl+U: everything left of the caret.</summary>
    public void DeleteToStart()
    {
        _text.Remove(0, Caret);
        Caret = 0;
    }

    /// <summary>
    /// Ctrl+W: skips back over any whitespace, then deletes the word before it.
    /// </summary>
    /// <remarks>
    /// The separating space is left behind, as readline leaves it: "baud 2400" becomes
    /// "baud " and is ready for another rate.
    /// </remarks>
    public void DeleteWordBack()
    {
        var end = Caret;
        while (Caret > 0 && char.IsWhiteSpace(_text[Caret - 1]))
        {
            Caret--;
        }

        while (Caret > 0 && !char.IsWhiteSpace(_text[Caret - 1]))
        {
            Caret--;
        }

        _text.Remove(Caret, end - Caret);
    }

    public void Clear()
    {
        _text.Clear();
        Caret = 0;
        _historyIndex = _history.Count;
        _stashed = string.Empty;
    }

    /// <summary>Walks back through the history. False when there is nowhere further to go.</summary>
    public bool HistoryPrevious()
    {
        if (_historyIndex == 0)
        {
            return false;
        }

        if (_historyIndex == _history.Count)
        {
            _stashed = Text;
        }

        _historyIndex--;
        Set(_history[_historyIndex]);
        return true;
    }

    /// <summary>Walks forward, ending on the line that was being typed when the walk began.</summary>
    public bool HistoryNext()
    {
        if (_historyIndex >= _history.Count)
        {
            return false;
        }

        _historyIndex++;
        Set(_historyIndex == _history.Count ? _stashed : _history[_historyIndex]);
        return true;
    }

    public Completion Complete(CommandVocabulary vocabulary)
    {
        var completion = CommandCompleter.Complete(Text, Caret, vocabulary);
        Set(completion.Line);
        Caret = completion.Caret;
        return completion;
    }

    /// <summary>
    /// Takes the line, remembers it, and empties the editor.
    /// </summary>
    /// <remarks>
    /// A blank line and an immediate repeat are both left out of the history: neither is
    /// something anyone scrolls back for.
    /// </remarks>
    public string Submit()
    {
        var line = Text;

        if (!string.IsNullOrWhiteSpace(line)
            && (_history.Count == 0 || !_history[^1].Equals(line, StringComparison.Ordinal)))
        {
            _history.Add(line);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
            }
        }

        Clear();
        return line;
    }

    private void Set(string text)
    {
        _text.Clear();
        _text.Append(text);
        Caret = _text.Length;
    }
}
