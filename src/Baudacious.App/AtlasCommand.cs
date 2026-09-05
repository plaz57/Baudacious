using System.Globalization;
using System.Runtime.Versioning;
using Baudacious.Render;

namespace Baudacious.App;

/// <summary>
/// Builds the glyph atlas and writes it out as a PNG.
/// </summary>
/// <remarks>
/// A development aid, and the only practical way to check the atlas before there is a
/// renderer to draw it with: the synthesised box-drawing joins either line up or they
/// don't, and that is far easier to see than to assert.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class AtlasCommand
{
    public static int Run(string[] args)
    {
        var path = args.Length > 0 ? args[0] : "atlas.png";
        var scale = args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out var s) ? s : 1;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Always build the real 8x16 cell; `scale` only magnifies the inspection image,
        // so what is looked at is exactly what the renderer will sample.
        var atlas = GlyphAtlas.Build();
        atlas.SaveDebugImage(path, scale);

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{atlas.Width}x{atlas.Height} atlas, {atlas.CellWidth}x{atlas.CellHeight} cells -> {path}"));

        return 0;
    }
}
