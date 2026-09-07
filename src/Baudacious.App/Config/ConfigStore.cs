using System.Globalization;
using Baudacious.Core.Config;

namespace Baudacious.App.Config;

/// <summary>
/// Where the settings live, and the order they are read in.
/// </summary>
/// <remarks>
/// Two files, and the separation matters. <c>baudacious.ini</c> is the user's: hand-edited,
/// commented, and never written to after it is first created. <c>state.ini</c> is ours:
/// what the last session was left set to, rewritten on every exit. Anything else would mean
/// the application reformatting a file the user is in the middle of editing.
/// <para>
/// Precedence runs defaults, then <c>baudacious.ini</c>, then <c>state.ini</c>, then the
/// command line. Each layer replaces only what it says, so on a fresh install with no state
/// the configuration file's values are what take effect — the behaviour the spec asks for,
/// and here a consequence of the ordering rather than a rule of its own.
/// </para>
/// <para>
/// With one exception, and it is the interesting one. State beating the file means that once
/// a session has saved a palette, editing the palette in the file could never do anything —
/// which contradicts hot reload, where saving an edit is supposed to change the display
/// within a second. So an edit that <em>changes</em> a value the state also holds takes that
/// setting away from the state for the rest of the run: the user has just said what they
/// want, in writing. Settings they did not touch keep following the state as before, and the
/// next exit writes the whole lot out again.
/// </para>
/// </remarks>
public sealed class ConfigStore
{
    /// <summary>Everything that is persisted between runs, and nothing that is not.</summary>
    private static readonly (string Section, string Key)[] StateKeys =
    [
        ("line", "baud"),
        ("line", "framing"),
        ("display", "phosphor"),
        ("display", "font"),
        ("display", "font-scale"),
        ("crt", "enabled"),
        ("crt", "bloom"),
        ("crt", "scanline"),
        ("crt", "vignette"),
        ("crt", "curvature"),
        ("crt", "ambient-glow"),
        ("window", "columns"),
        ("window", "rows"),
        ("window", "fullscreen"),
    ];

    /// <summary>The configuration file as last read, for telling an edit from a re-read.</summary>
    private IniDocument _config = IniDocument.Empty();

    /// <summary>
    /// Settings the user has written down during this run, which the state no longer gets a
    /// say in.
    /// </summary>
    private readonly HashSet<(string Section, string Key)> _edited = [];

    private ConfigStore(string directory)
    {
        Directory = directory;
        ConfigPath = Path.Combine(directory, "baudacious.ini");
        StatePath = Path.Combine(directory, "state.ini");
    }

    public string Directory { get; }

    /// <summary>The user's file. Read, and created once; never rewritten.</summary>
    public string ConfigPath { get; }

    /// <summary>Ours. Rewritten on exit.</summary>
    public string StatePath { get; }

    /// <summary>Everything wrong with either file, in the order it was found.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>The resolved settings, defaults included where nothing said otherwise.</summary>
    public Settings Settings { get; private set; } = Settings.Defaults;

    /// <summary>Whether the configuration file was written by this run.</summary>
    public bool CreatedConfig { get; private set; }

    /// <summary>The settings a state file records, for anyone who needs the list.</summary>
    public static IEnumerable<(string Section, string Key)> PersistedKeys => StateKeys;

    /// <summary>%APPDATA%\Baudacious, or a folder beside the executable if there is none.</summary>
    public static string DefaultDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return string.IsNullOrEmpty(appData)
            ? Path.Combine(AppContext.BaseDirectory, "config")
            : Path.Combine(appData, "Baudacious");
    }

    /// <summary>
    /// Reads both files, creating the configuration file if this is a first run.
    /// </summary>
    /// <remarks>
    /// Never throws for anything the user could have caused. A directory that cannot be
    /// created, a file that cannot be read, a file full of nonsense: each is reported and
    /// the application starts anyway, because a terminal that will not open because of its
    /// own settings file is worse than one that opens with the defaults.
    /// </remarks>
    public static ConfigStore Load(string? directory = null)
    {
        var store = new ConfigStore(directory ?? DefaultDirectory());
        var problems = new List<string>();

        store.EnsureConfigExists(problems);

        var settings = store.ReadConfig(Settings.Defaults, problems);
        settings = store.ReadState(settings, problems);

        store.Settings = settings;
        store.Problems = problems;
        return store;
    }

    /// <summary>
    /// Re-reads the configuration file for a hot reload, keeping the persisted state on top
    /// of everything the user has not edited.
    /// </summary>
    /// <remarks>
    /// Returns null when the file could not be read at all. An editor saving by atomic
    /// replace leaves it missing for an instant, and falling back to defaults on a timing
    /// accident would be far worse than waiting for the next change.
    /// </remarks>
    public Settings? Reread(List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (TryRead(ConfigPath, "baudacious.ini", problems) is not { } ini)
        {
            return null;
        }

        var settings = AdoptConfig(ini, Settings.Defaults, problems, noticeEdits: true);
        return ReadState(settings, problems);
    }

    /// <summary>
    /// Writes the persisted subset of the settings, leaving the user's file alone.
    /// </summary>
    /// <remarks>
    /// The existing state file is parsed and assigned into rather than regenerated, so
    /// anything anyone has added to it survives. Failure is silent by design: nobody wants a
    /// dialog about a state file while they are closing a window.
    /// </remarks>
    public void SaveState(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var ini = File.Exists(StatePath)
                ? IniDocument.Parse(Read(StatePath))
                : NewStateFile();

            var display = settings.Display;
            var crt = display.Crt;

            ini.Set("line", "baud", settings.Rate.IsUnlimited
                ? "unlimited"
                : settings.Rate.Baud.ToString(CultureInfo.InvariantCulture));
            ini.Set("line", "framing", settings.Rate.Framing.ToString());
            ini.Set("display", "phosphor", display.Phosphor.ToString().ToLowerInvariant());
            ini.Set("display", "font", settings.Font);
            ini.Set("display", "font-scale", display.FontScale.ToString(CultureInfo.InvariantCulture));
            ini.Set("crt", "enabled", Bool(crt.Enabled));
            ini.Set("crt", "bloom", Fraction(crt.Bloom));
            ini.Set("crt", "scanline", Fraction(crt.Scanline));
            ini.Set("crt", "vignette", Fraction(crt.Vignette));
            ini.Set("crt", "curvature", Fraction(crt.Curvature));
            ini.Set("crt", "ambient-glow", Fraction(crt.AmbientGlow));
            ini.Set("window", "columns", settings.Window.Columns.ToString(CultureInfo.InvariantCulture));
            ini.Set("window", "rows", settings.Window.Rows.ToString(CultureInfo.InvariantCulture));
            ini.Set("window", "fullscreen", Bool(settings.Window.Fullscreen));

            File.WriteAllText(StatePath, ini.ToText());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the last rate is a small thing; failing to close is not.
        }
    }

    private static IniDocument NewStateFile() => IniDocument.Parse(
        "; Written by Baudacious when it exits. Your settings are in baudacious.ini;\n" +
        "; this file only remembers what the last session was left set to, and takes\n" +
        "; precedence over that file. Safe to delete.\n");

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Fraction(double value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Reads a file that another process may be writing at this moment.</summary>
    private static string Read(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Parses a file, or returns null if it is not there or will not open.
    /// </summary>
    /// <remarks>
    /// Parse problems are reported and the document still comes back, because a file with
    /// one bad line in it is still mostly settings.
    /// </remarks>
    private static IniDocument? TryRead(string path, string source, List<string> problems)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string text;
        try
        {
            text = Read(path);
        }
        catch (IOException)
        {
            // Almost certainly a save in progress. Not worth reporting.
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            problems.Add($"could not read {source}: {ex.Message}");
            return null;
        }

        var ini = IniDocument.Parse(text);

        foreach (var problem in ini.Problems)
        {
            problems.Add($"{source} {problem}");
        }

        return ini;
    }

    private void EnsureConfigExists(List<string> problems)
    {
        if (File.Exists(ConfigPath))
        {
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(ConfigPath, SettingsTemplate.Build());
            CreatedConfig = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"could not create {ConfigPath}: {ex.Message}");
        }
    }

    private Settings ReadConfig(Settings baseline, List<string> problems) =>
        TryRead(ConfigPath, "baudacious.ini", problems) is { } ini
            ? AdoptConfig(ini, baseline, problems, noticeEdits: false)
            : baseline;

    /// <summary>
    /// Applies the user's file, and on a re-read notes which persisted settings it changed.
    /// </summary>
    /// <param name="noticeEdits">
    /// False on the first read, where there is nothing to compare against and every setting
    /// in the file would look like a fresh edit — which would take the whole state file out
    /// of play before it had been applied once.
    /// </param>
    private Settings AdoptConfig(
        IniDocument ini, Settings baseline, List<string> problems, bool noticeEdits)
    {
        if (noticeEdits)
        {
            foreach (var (section, key) in StateKeys)
            {
                var now = ini.Get(section, key);

                // A value that changed is an instruction. A value that was deleted is the
                // withdrawal of one, and should leave whatever sits above in charge rather
                // than resetting a rate the user set with alt+B ten minutes ago.
                if (now is not null
                    && !string.Equals(_config.Get(section, key), now, StringComparison.Ordinal))
                {
                    _edited.Add((section, key));
                }
            }
        }

        _config = ini;
        return SettingsReader.Apply(baseline, ini, "baudacious.ini", problems);
    }

    /// <summary>
    /// Applies what the last session left, minus anything the user has since written down.
    /// </summary>
    private Settings ReadState(Settings baseline, List<string> problems)
    {
        if (TryRead(StatePath, "state.ini", problems) is not { } ini)
        {
            return baseline;
        }

        foreach (var (section, key) in _edited)
        {
            ini.Remove(section, key);
        }

        return SettingsReader.Apply(baseline, ini, "state.ini", problems);
    }
}
