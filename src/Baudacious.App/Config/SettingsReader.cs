using System.Globalization;
using Baudacious.Core.Baud;
using Baudacious.Core.Config;
using Baudacious.Render;

namespace Baudacious.App.Config;

/// <summary>
/// Lays one INI file over a set of settings.
/// </summary>
/// <remarks>
/// A layer only replaces what it actually says, so precedence — persisted state over the
/// hand-edited file over the built-in defaults — falls out of the order the layers are
/// applied in rather than needing rules of its own.
/// <para>
/// A value that will not parse is reported and skipped, leaving whatever the layer beneath
/// had. That is deliberate: one bad line should cost the user that line, not the file. The
/// report names the value, the setting, the line it is on and what was used instead, so the
/// message is enough to go and fix it without hunting.
/// </para>
/// </remarks>
public static class SettingsReader
{
    /// <summary>The names a phosphor may be given, lowercase.</summary>
    public static readonly string[] PhosphorNames = [.. Enum.GetNames<Phosphor>().Select(n => n.ToLowerInvariant())];

    /// <summary>The names a cursor shape may be given, lowercase.</summary>
    public static readonly string[] CursorNames = [.. Enum.GetNames<CursorShape>().Select(n => n.ToLowerInvariant())];

    /// <summary>The keys a custom shell profile understands.</summary>
    private static readonly string[] ProfileKeys = ["command", "arguments", "like"];

    /// <summary>
    /// Every section and the settings it holds, for telling a typo from a setting.
    /// </summary>
    /// <remarks>
    /// A second list of the same names, which is a thing to keep in step — so a test walks
    /// the generated default file against it and fails if either side gains a name the
    /// other has not. Worth that, because a misplaced key is the one mistake the reader
    /// would otherwise pass over in silence: putting <c>scanline</c> under <c>[display]</c>
    /// instead of <c>[crt]</c> does exactly nothing and says exactly nothing.
    /// </remarks>
    private static readonly (string Section, string[] Keys)[] Schema =
    [
        ("line", ["baud", "framing"]),
        ("shell", ["default", "fast-echo"]),
        ("display", ["phosphor", "font", "font-scale", "cursor", "cursor-blink", "scrollback"]),
        ("crt", ["enabled", "bloom", "scanline", "vignette", "ambient-glow"]),
        ("window", ["columns", "rows", "fullscreen"]),
    ];

    /// <summary>
    /// Applies a file to a baseline.
    /// </summary>
    /// <param name="source">
    /// How to refer to the file in a problem report — its name, not its whole path, which
    /// would push the message off the screen.
    /// </param>
    public static Settings Apply(
        Settings baseline, IniDocument ini, string source, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(ini);
        ArgumentNullException.ThrowIfNull(problems);

        ReportUnknown(ini, source, problems);

        var reader = new Layer(ini, source, problems);
        var settings = baseline;

        settings = settings with
        {
            Rate = reader.Rate("line", "baud", "framing", settings.Rate),
            Profile = reader.Text("shell", "default", settings.Profile),
            FastEcho = reader.FastEcho("shell", "fast-echo", settings.FastEcho),
            Font = reader.Face("display", "font", settings.Font),
            Scrollback = reader.Integer("display", "scrollback", 100, 1_000_000, settings.Scrollback),
            Display = reader.Display(settings.Display),
            Window = reader.Window(settings.Window),
            Keys = reader.Keys(settings.Keys),
            Profiles = reader.Profiles(settings.Profiles),
        };

        return settings;
    }

    /// <summary>
    /// Names a setting or section that nothing will ever read.
    /// </summary>
    /// <remarks>
    /// Reported, never fatal, and never a reason to change the file. Unknown sections and
    /// keys are exactly what a hand-edited file collects, and the useful thing to do about
    /// one is to say where it is.
    /// </remarks>
    private static void ReportUnknown(IniDocument ini, string source, List<string> problems)
    {
        foreach (var key in ini.Keys(string.Empty))
        {
            problems.Add($"{source} line {ini.LineOf(string.Empty, key)}: " +
                         $"'{key}' is above every section, so nothing reads it");
        }

        foreach (var section in ini.Sections())
        {
            var known = KeysOf(section);

            if (known is null)
            {
                problems.Add($"{source}: no such section as [{section}]");
                continue;
            }

            foreach (var key in ini.Keys(section))
            {
                if (!known.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    problems.Add($"{source} line {ini.LineOf(section, key)}: " +
                                 $"[{section}] has no setting called '{key}'");
                }
            }
        }
    }

    /// <summary>What a section may contain, or null if there is no such section.</summary>
    private static IReadOnlyList<string>? KeysOf(string section)
    {
        if (section.StartsWith("profile.", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileKeys;
        }

        if (section.Equals("keys", StringComparison.OrdinalIgnoreCase))
        {
            return [.. KeyBindings.Actions.Select(KeyBindings.NameOf)];
        }

        foreach (var (name, keys) in Schema)
        {
            if (name.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                return keys;
            }
        }

        return null;
    }

    /// <summary>One file's worth of reading, with somewhere to put the complaints.</summary>
    private sealed class Layer(IniDocument ini, string source, List<string> problems)
    {
        public string Text(string section, string key, string current) =>
            ini.Get(section, key) is { Length: > 0 } value ? value : current;

        /// <summary>A bundled typeface by name.</summary>
        public string Face(string section, string key, string current)
        {
            if (ini.Get(section, key) is not { Length: > 0 } value)
            {
                return current;
            }

            if (FontCatalog.Find(value) is { } face)
            {
                return face.Name;
            }

            Complain(section, key, value, $"no such font, try {string.Join(", ", FontCatalog.Names)}");
            return current;
        }

        public LineRate Rate(string section, string baudKey, string framingKey, LineRate current)
        {
            var rate = current;

            if (ini.Get(section, baudKey) is { Length: > 0 } baud)
            {
                if (LineRate.TryParse(baud, out var parsed))
                {
                    // A bare rate keeps whatever framing is already in force rather than
                    // reverting to the one conventionally paired with it, so the two
                    // settings stay independent the way the menus are. A rate written with
                    // a framing beside it, as "1200 8N2", of course means that framing —
                    // and LineRate cannot tell the two cases apart, so the text is asked.
                    var stated = baud.Split(
                        [' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries).Length > 1;

                    rate = parsed.IsUnlimited
                        ? LineRate.Unlimited
                        : LineRate.Of(
                            parsed.Baud,
                            stated || rate.IsUnlimited ? parsed.Framing : rate.Framing);
                }
                else
                {
                    Complain(section, baudKey, baud, $"not a rate, using {current}");
                }
            }

            if (ini.Get(section, framingKey) is { Length: > 0 } framing)
            {
                if (Framing.TryParse(framing, out var parsed))
                {
                    rate = rate.IsUnlimited ? rate : LineRate.Of(rate.Baud, parsed);
                }
                else
                {
                    Complain(section, framingKey, framing, "not a framing, try 8N1 or 7E1");
                }
            }

            return rate;
        }

        /// <summary>Three states, because letting the rate decide is one of them.</summary>
        public bool? FastEcho(string section, string key, bool? current)
        {
            if (ini.Get(section, key) is not { Length: > 0 } value)
            {
                return current;
            }

            switch (value.ToLowerInvariant())
            {
                case "auto": return null;
                case "on" or "true" or "yes": return true;
                case "off" or "false" or "no": return false;
                default:
                    Complain(section, key, value, "expected auto, on or off");
                    return current;
            }
        }

        public DisplaySettings Display(DisplaySettings current)
        {
            var blink = Integer("display", "cursor-blink", 0, 10_000, (int)current.CursorBlinkInterval.TotalMilliseconds);

            return current with
            {
                Phosphor = Choice("display", "phosphor", current.Phosphor),
                FontScale = Integer("display", "font-scale", 1, 8, current.FontScale),
                CursorShape = Choice("display", "cursor", current.CursorShape),

                // Zero means a steady cursor. Keeping the interval as well as the flag
                // means turning blink back on restores the rate it had.
                CursorBlink = blink > 0,
                CursorBlinkInterval = blink > 0 ? TimeSpan.FromMilliseconds(blink) : current.CursorBlinkInterval,

                Crt = current.Crt with
                {
                    Enabled = Flag("crt", "enabled", current.Crt.Enabled),
                    Bloom = Fraction("crt", "bloom", current.Crt.Bloom),
                    Scanline = Fraction("crt", "scanline", current.Crt.Scanline),
                    Vignette = Fraction("crt", "vignette", current.Crt.Vignette),
                    AmbientGlow = Fraction("crt", "ambient-glow", current.Crt.AmbientGlow),
                },
            };
        }

        public WindowGeometry Window(WindowGeometry current) => current with
        {
            Columns = Integer("window", "columns", 20, 1000, current.Columns),
            Rows = Integer("window", "rows", 5, 500, current.Rows),
            Fullscreen = Flag("window", "fullscreen", current.Fullscreen),
        };

        public KeyBindings Keys(KeyBindings current)
        {
            var bindings = current;

            foreach (var action in KeyBindings.Actions)
            {
                var key = KeyBindings.NameOf(action);
                if (ini.Get("keys", key) is not { Length: > 0 } value)
                {
                    continue;
                }

                if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    bindings = bindings.With(action, KeyChord.None);
                }
                else if (KeyChord.TryParse(value, out var chord))
                {
                    bindings = bindings.With(action, chord);
                }
                else
                {
                    Complain("keys", key, value, "not a key chord, try alt+B or ctrl+]");
                }
            }

            // Reported rather than resolved: which of two actions should lose is the user's
            // call, and silently dropping one would be a worse surprise than being told.
            foreach (var (first, second, chord) in bindings.Conflicts())
            {
                problems.Add(
                    $"{source}: {KeyBindings.NameOf(first)} and {KeyBindings.NameOf(second)} " +
                    $"are both on {chord}; only {KeyBindings.NameOf(first)} will fire");
            }

            return bindings;
        }

        public IReadOnlyList<ShellProfileConfig> Profiles(IReadOnlyList<ShellProfileConfig> current)
        {
            List<ShellProfileConfig>? found = null;

            foreach (var section in ini.Sections())
            {
                if (!section.StartsWith("profile.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = section["profile.".Length..].Trim();
                if (name.Length == 0)
                {
                    problems.Add($"{source}: [{section}] does not name a profile");
                    continue;
                }

                if (ini.Get(section, "command") is not { Length: > 0 } command)
                {
                    problems.Add($"{source}: [{section}] needs a 'command'; ignoring it");
                    continue;
                }

                found ??= [];
                found.Add(new ShellProfileConfig(
                    name,
                    command,
                    ini.Get(section, "arguments") ?? string.Empty,
                    ini.Get(section, "like") is { Length: > 0 } like ? like : null));
            }

            return found ?? current;
        }

        public int Integer(string section, string key, int min, int max, int current)
        {
            if (ini.Get(section, key) is not { Length: > 0 } value)
            {
                return current;
            }

            if (!int.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
            {
                Complain(section, key, value, $"not a whole number, using {current}");
                return current;
            }

            if (parsed < min || parsed > max)
            {
                // Clamping quietly would leave someone who asked for 20 believing they got
                // it, which is the same reasoning the local prompt uses.
                Complain(section, key, value, $"outside {min} to {max}, using {current}");
                return current;
            }

            return parsed;
        }

        /// <summary>An intensity, which is always 0 to 1.</summary>
        public double Fraction(string section, string key, double current)
        {
            if (ini.Get(section, key) is not { Length: > 0 } value)
            {
                return current;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                Complain(
                    section,
                    key,
                    value,
                    $"not a number, using {current.ToString("0.##", CultureInfo.InvariantCulture)}");
                return current;
            }

            if (parsed is < 0 or > 1)
            {
                Complain(section, key, value, "outside 0 to 1");
                return current;
            }

            return parsed;
        }

        public bool Flag(string section, string key, bool current)
        {
            if (ini.Get(section, key) is not { Length: > 0 } value)
            {
                return current;
            }

            switch (value.ToLowerInvariant())
            {
                case "true" or "yes" or "on" or "1": return true;
                case "false" or "no" or "off" or "0": return false;
                default:
                    Complain(section, key, value, "expected true or false");
                    return current;
            }
        }

        public T Choice<T>(string section, string key, T current)
            where T : struct, Enum
        {
            if (ini.Get(section, key) is not { Length: > 0 } value)
            {
                return current;
            }

            if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            var names = string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()));
            Complain(section, key, value, $"expected one of {names}");
            return current;
        }

        private void Complain(string section, string key, string value, string why)
        {
            var line = ini.LineOf(section, key);
            var at = line > 0 ? $" line {line}" : string.Empty;
            problems.Add($"{source}{at}: {key} '{value}': {why}");
        }
    }
}
