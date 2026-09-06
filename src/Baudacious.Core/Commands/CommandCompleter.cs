using System.Globalization;
using Baudacious.Core.Baud;

namespace Baudacious.Core.Commands;

/// <summary>
/// The result of one Tab: the line as it now stands, and the candidates worth showing.
/// </summary>
/// <param name="Candidates">
/// Empty when there was nothing to offer or exactly one thing, which was simply taken.
/// </param>
public readonly record struct Completion(string Line, int Caret, IReadOnlyList<string> Candidates);

/// <summary>
/// Completes the word under the caret against what can legally appear in that position.
/// </summary>
public static class CommandCompleter
{
    private static readonly string[] Framings = ["8N1", "7E1", "8N2", "8E1"];
    private static readonly string[] OnOff = ["on", "off"];

    public static Completion Complete(string line, int caret, CommandVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(vocabulary);

        caret = Math.Clamp(caret, 0, line.Length);

        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(line[start - 1]))
        {
            start--;
        }

        var prefix = line[start..caret];
        var position = CommandParser.Tokenize(line[..start]).Count;
        var candidates = CandidatesAt(line, position, vocabulary)
            .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return new Completion(line, caret, []);
        }

        // One match is taken outright, with a space, because the next thing wanted is
        // almost always the next word.
        if (candidates.Count == 1)
        {
            return Replace(line, start, caret, candidates[0] + " ", []);
        }

        // Several: take as much as they agree on, and show the rest. Typing one more
        // character then Tab again narrows it, which is the habit every shell teaches.
        var common = LongestCommonPrefix(candidates);
        return common.Length > prefix.Length
            ? Replace(line, start, caret, common, candidates)
            : new Completion(line, caret, candidates);
    }

    private static Completion Replace(
        string line, int start, int caret, string replacement, IReadOnlyList<string> candidates)
    {
        var rebuilt = string.Concat(line.AsSpan(0, start), replacement, line.AsSpan(caret));
        return new Completion(rebuilt, start + replacement.Length, candidates);
    }

    private static IReadOnlyList<string> CandidatesAt(
        string line, int position, CommandVocabulary vocabulary)
    {
        if (position == 0)
        {
            return CommandVocabulary.Verbs;
        }

        var tokens = CommandParser.Tokenize(line);
        if (tokens.Count == 0 || !CommandParser.TryResolveVerb(tokens[0], out var verb, out _))
        {
            return [];
        }

        if (position == 1)
        {
            return verb switch
            {
                "baud" => Rates(),
                "framing" => Framings,
                "phosphor" => vocabulary.Phosphors,
                "crt" or "fastecho" => OnOff,
                "fontsize" => FontSizes(),
                "session" => ["new"],
                _ => [],
            };
        }

        // session new <profile>. Nothing else takes a second argument, since a rate's
        // optional framing is completed as part of the rate.
        return position == 2 && verb == "session" && tokens.Count > 1
               && "new".StartsWith(tokens[1], StringComparison.OrdinalIgnoreCase)
            ? vocabulary.Profiles
            : [];
    }

    private static string[] Rates()
    {
        var rates = new string[LineRate.StandardBauds.Length + 1];
        for (var i = 0; i < LineRate.StandardBauds.Length; i++)
        {
            rates[i] = LineRate.StandardBauds[i].ToString(CultureInfo.InvariantCulture);
        }

        rates[^1] = "unlimited";
        return rates;
    }

    private static string[] FontSizes()
    {
        var sizes = new string[CommandParser.MaxFontScale];
        for (var i = 0; i < sizes.Length; i++)
        {
            sizes[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        return sizes;
    }

    private static string LongestCommonPrefix(List<string> candidates)
    {
        var shortest = candidates[0];
        foreach (var candidate in candidates)
        {
            if (candidate.Length < shortest.Length)
            {
                shortest = candidate;
            }
        }

        var length = shortest.Length;
        foreach (var candidate in candidates)
        {
            var i = 0;
            while (i < length && char.ToLowerInvariant(candidate[i]) == char.ToLowerInvariant(shortest[i]))
            {
                i++;
            }

            length = i;
        }

        return shortest[..length];
    }
}
