using System.Globalization;
using Baudacious.Core.Baud;

namespace Baudacious.Core.Commands;

/// <summary>
/// Turns a typed line into a <see cref="LocalCommand"/>, or into the reason it is not one.
/// </summary>
public static class CommandParser
{
    /// <summary>The largest magnification the renderer will accept.</summary>
    public const int MaxFontScale = 8;

    public static LocalCommand Parse(string? line) => Parse(line, CommandVocabulary.Empty);

    public static LocalCommand Parse(string? line, CommandVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        var tokens = Tokenize(line);
        if (tokens.Count == 0)
        {
            return new LocalCommand.Nothing();
        }

        if (!TryResolveVerb(tokens[0], out var verb, out var failure))
        {
            return failure!;
        }

        var arguments = tokens.GetRange(1, tokens.Count - 1);

        return verb switch
        {
            "baud" => ParseBaud(arguments),
            "framing" => ParseFraming(arguments),
            "flush" => NoArguments(arguments, verb) ?? new LocalCommand.Flush(),
            "phosphor" => ParsePhosphor(arguments, vocabulary),
            "fontsize" => ParseFontSize(arguments),
            "crt" => ParseCrt(arguments),
            "session" => ParseSession(arguments, vocabulary),
            "help" => NoArguments(arguments, verb) ?? new LocalCommand.ShowHelp(),
            _ => NoArguments(arguments, verb) ?? new LocalCommand.Quit(),
        };
    }

    /// <summary>Splits on runs of whitespace, discarding them.</summary>
    public static List<string> Tokenize(string? line) =>
        string.IsNullOrWhiteSpace(line)
            ? []
            : [.. line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>
    /// Matches a verb, accepting any unambiguous abbreviation.
    /// </summary>
    /// <remarks>
    /// <c>b</c> is baud and <c>q</c> is quit, but <c>f</c> is three different things and
    /// saying so is more use than guessing.
    /// </remarks>
    public static bool TryResolveVerb(string word, out string verb, out LocalCommand? failure)
    {
        verb = string.Empty;
        failure = null;

        var lowered = word.ToLowerInvariant();

        foreach (var candidate in CommandVocabulary.Verbs)
        {
            if (candidate.Equals(lowered, StringComparison.Ordinal))
            {
                verb = candidate;
                return true;
            }
        }

        var matches = CommandVocabulary.Verbs
            .Where(c => c.StartsWith(lowered, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 1)
        {
            verb = matches[0];
            return true;
        }

        failure = matches.Count > 1
            ? new LocalCommand.Failed($"'{word}' is ambiguous: {string.Join(", ", matches)}")
            : new LocalCommand.Failed($"no such command '{word}' - try 'help'");

        return false;
    }

    private static LocalCommand? NoArguments(List<string> arguments, string verb) =>
        arguments.Count == 0 ? null : new LocalCommand.Failed($"'{verb}' takes no arguments");

    private static LocalCommand ParseBaud(List<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return new LocalCommand.Failed("baud <rate|unlimited>, optionally with a framing");
        }

        // LineRate reads "2400" and "2400 8N1" alike, which is why the arguments are handed
        // back over joined rather than picked apart here.
        return LineRate.TryParse(string.Join(' ', arguments), out var rate)
            ? new LocalCommand.SetRate(rate)
            : new LocalCommand.Failed(
                $"'{string.Join(' ', arguments)}' is not a rate - a number up to {LineRate.MaxBaud}, or 'unlimited'");
    }

    private static LocalCommand ParseFraming(List<string> arguments)
    {
        if (arguments.Count != 1)
        {
            return new LocalCommand.Failed("framing <8N1|7E1|8N2|8E1>");
        }

        return Framing.TryParse(arguments[0], out var framing)
            ? new LocalCommand.SetFraming(framing)
            : new LocalCommand.Failed($"'{arguments[0]}' is not a framing - try 8N1 or 7E1");
    }

    private static LocalCommand ParsePhosphor(List<string> arguments, CommandVocabulary vocabulary)
    {
        var known = vocabulary.Phosphors;

        if (arguments.Count != 1)
        {
            return new LocalCommand.Failed(
                known.Count == 0 ? "phosphor <name>" : $"phosphor <{string.Join('|', known)}>");
        }

        foreach (var name in known)
        {
            if (name.Equals(arguments[0], StringComparison.OrdinalIgnoreCase))
            {
                return new LocalCommand.SetPhosphor(name);
            }
        }

        return new LocalCommand.Failed(
            known.Count == 0
                ? $"no phosphor named '{arguments[0]}'"
                : $"no phosphor named '{arguments[0]}' - {string.Join(", ", known)}");
    }

    private static LocalCommand ParseFontSize(List<string> arguments)
    {
        if (arguments.Count != 1
            || !int.TryParse(arguments[0], CultureInfo.InvariantCulture, out var scale))
        {
            return new LocalCommand.Failed($"fontsize <1-{MaxFontScale}>");
        }

        // Clamping silently would leave someone who typed 20 believing they got 20.
        return scale is >= 1 and <= MaxFontScale
            ? new LocalCommand.SetFontSize(scale)
            : new LocalCommand.Failed($"fontsize is 1 to {MaxFontScale}; {scale} is outside it");
    }

    private static LocalCommand ParseCrt(List<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return new LocalCommand.SetCrt(null);
        }

        if (arguments.Count > 1)
        {
            return new LocalCommand.Failed("crt [on|off]");
        }

        return arguments[0].ToLowerInvariant() switch
        {
            "on" => new LocalCommand.SetCrt(true),
            "off" => new LocalCommand.SetCrt(false),
            _ => new LocalCommand.Failed($"'{arguments[0]}' is not on or off"),
        };
    }

    private static LocalCommand ParseSession(List<string> arguments, CommandVocabulary vocabulary)
    {
        if (arguments.Count == 0 || !"new".StartsWith(arguments[0], StringComparison.OrdinalIgnoreCase))
        {
            return new LocalCommand.Failed("session new <profile>");
        }

        if (arguments.Count == 1)
        {
            return new LocalCommand.Failed(
                vocabulary.Profiles.Count == 0
                    ? "session new <profile>"
                    : $"session new <{string.Join('|', vocabulary.Profiles)}>");
        }

        if (arguments.Count > 2)
        {
            return new LocalCommand.Failed("session new takes one profile");
        }

        var profile = arguments[1];

        // A profile may name a variant after a colon, as wsl:Ubuntu does; only the part
        // before it has to be a profile we ship.
        var colon = profile.IndexOf(':', StringComparison.Ordinal);
        var head = colon < 0 ? profile : profile[..colon];

        if (vocabulary.Profiles.Count > 0
            && !vocabulary.Profiles.Any(p => p.Equals(head, StringComparison.OrdinalIgnoreCase)))
        {
            return new LocalCommand.Failed(
                $"no profile named '{head}' - {string.Join(", ", vocabulary.Profiles)}");
        }

        return new LocalCommand.NewSession(profile);
    }
}
