namespace Baudacious.Render;

/// <summary>One bundled bitmap face: what to call it, and whose it is.</summary>
/// <param name="Name">The name in the settings file and the local prompt.</param>
/// <param name="Resource">The embedded <c>.hex</c> resource.</param>
/// <param name="Stem">
/// Stem width in pixels at 8x16. The whole reason there is a choice here: a two-pixel stem
/// doubles again at 2x magnification, and some people find that too heavy to read all day.
/// </param>
/// <param name="Credit">Author and licence, short enough for the about panel.</param>
public sealed record BitmapFace(string Name, string Resource, int Stem, string Credit);

/// <summary>
/// The faces Baudacious ships with.
/// </summary>
/// <remarks>
/// All three are 8x16 and all three are permissively licensed, which is the constraint that
/// decided the list. The interesting axis between them is stem width: unscii and Spleen draw
/// capitals with two-pixel stems, Terminus with one, and at 2x magnification that is the
/// difference between four pixels and two. Full licence texts are in
/// <c>assets/licenses/</c>; the provenance and the conversion are recorded in
/// <c>assets/README.md</c>.
/// </remarks>
public static class FontCatalog
{
    public static readonly BitmapFace[] Faces =
    [
        new("unscii", "Baudacious.Render.unscii-16.hex", 2, "unscii-16 by Viznut, public domain"),
        new("terminus", "Baudacious.Render.terminus-16.hex", 1, "ter-u16n from Terminus Font by Dimitar Toshkov Zhekov, OFL 1.1"),
        new("spleen", "Baudacious.Render.spleen-16.hex", 2, "Spleen 8x16 by Frederic Cambus, BSD 2-Clause"),
    ];

    /// <summary>What Baudacious draws with when nothing says otherwise.</summary>
    public static BitmapFace Default => Faces[0];

    public static IEnumerable<string> Names => Faces.Select(f => f.Name);

    /// <summary>The face by name, or null if there is no such face.</summary>
    public static BitmapFace? Find(string? name)
    {
        foreach (var face in Faces)
        {
            if (face.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return face;
            }
        }

        return null;
    }
}
