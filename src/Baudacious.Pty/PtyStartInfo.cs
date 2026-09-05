namespace Baudacious.Pty;

/// <summary>
/// How to launch a hosted shell. Mirrors the shell-profile fields from
/// specs/shell-hosting: executable, arguments, working directory, environment.
/// </summary>
public sealed record PtyStartInfo
{
    public required string Executable { get; init; }

    /// <summary>
    /// Arguments as a single command-line tail, already quoted. Baudacious builds the
    /// full command line as <c>"Executable" Arguments</c>.
    /// </summary>
    public string Arguments { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Extra environment variables layered over the current process environment.
    /// Null means inherit unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    public short Columns { get; init; } = 80;

    public short Rows { get; init; } = 24;

    /// <summary>
    /// Size of each ReadFile from the ConPTY output pipe. Only affects how finely
    /// arrival timestamps are resolved, never how many bytes arrive.
    /// </summary>
    public int ReadBufferSize { get; init; } = 4096;
}
