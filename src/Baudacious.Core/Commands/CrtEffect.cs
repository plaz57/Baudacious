namespace Baudacious.Core.Commands;

/// <summary>
/// The CRT effects that have an intensity, and how far each one may be turned up.
/// </summary>
/// <remarks>
/// One table, because there were three and they disagreed. The command line clamped every
/// intensity to 0–4, the configuration file refused anything over 1, and the README said
/// bloom was unbounded — so <c>--bloom 2.5</c> worked, <c>bloom = 2.5</c> in the file was
/// rejected by line number, and the documentation described a third behaviour. All three
/// now ask here.
/// <para>
/// Bloom is the odd one because it is additive: it adds light on top of the glyph rather
/// than scaling something, so values above 1 keep having an effect instead of saturating.
/// The other five are fractions of a thing that already exists and are meaningless past
/// 1.
/// </para>
/// </remarks>
public static class CrtEffect
{
    /// <summary>Bloom washes the screen out somewhere past 2; this leaves room to see that.</summary>
    public const double BloomCeiling = 4.0;

    /// <summary>Every effect with an intensity, in the order the config file lists them.</summary>
    public static readonly string[] Names =
        ["bloom", "scanline", "vignette", "curvature", "mask", "persistence"];

    /// <summary>The largest useful value for an effect.</summary>
    public static double CeilingOf(string? effect) =>
        "bloom".Equals(effect, StringComparison.OrdinalIgnoreCase) ? BloomCeiling : 1.0;

    /// <summary>The canonical spelling of a name, or null if it is not an effect.</summary>
    public static string? Canonical(string? word)
    {
        foreach (var name in Names)
        {
            if (name.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }
}
