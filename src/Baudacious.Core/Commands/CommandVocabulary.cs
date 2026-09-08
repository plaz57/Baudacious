namespace Baudacious.Core.Commands;

/// <summary>
/// The words the prompt knows: its own verbs, plus the value lists it cannot invent for
/// itself.
/// </summary>
/// <remarks>
/// Palettes live in the renderer and shell profiles live in the application, so the parser
/// is told about them rather than hard-coding a second copy that would drift.
/// </remarks>
public sealed record CommandVocabulary
{
    /// <summary>Every verb, in the order help should list them.</summary>
    public static readonly string[] Verbs =
    [
        "baud", "framing", "flush", "phosphor", "font", "fontsize", "crt", "fastecho",
        "session",
        "about", "help", "quit",
    ];

    /// <summary>One line of help per verb, in the same order.</summary>
    public static readonly string[] Usage =
    [
        "baud <rate|unlimited>   line rate, optionally with framing",
        "framing <8N1|7E1|...>   bits per character",
        "flush                   release everything still queued",
        "phosphor <name>         screen palette",
        "font <name>             bitmap typeface",
        "fontsize <1-8>          magnification of the 8x16 cell",
        "crt [on|off|<eff> 0-1]  the pass, or one effect; tab lists",
        "fastecho [on|off]       cheap line editing; bare toggles",
        "session new <profile>   a second shell",
        "about                   version, licence, typeface",
        "help                    this list",
        "quit                    close Baudacious",
    ];

    public IReadOnlyList<string> Phosphors { get; init; } = [];

    public IReadOnlyList<string> Profiles { get; init; } = [];

    public IReadOnlyList<string> Fonts { get; init; } = [];

    public static CommandVocabulary Empty { get; } = new();
}
