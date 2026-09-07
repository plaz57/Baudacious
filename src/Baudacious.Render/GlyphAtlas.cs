using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace Baudacious.Render;

/// <summary>
/// A 16x16 grid of CP437 glyphs, one byte of coverage per pixel.
/// </summary>
/// <remarks>
/// <para>
/// Glyphs come from one of the bundled 8x16 bitmap faces — by default <b>unscii-16</b>,
/// a hand-drawn face in the public domain
/// (see <see cref="HexFont"/>). A bitmap font is the right tool here: an outline typeface
/// rasterised to eight pixels wide has no room to make good decisions and comes out
/// mushy, whereas a bitmap face has every stem placed deliberately — and its box drawing
/// is built to join, so tables have no seams.
/// </para>
/// <para>
/// unscii covers 250 of the 256 CP437 slots. The handful it lacks are filled by
/// rasterising a system monospace face and collapsing that to 1-bit, so they sit beside
/// the bitmap glyphs without looking blurred. Nothing above this class knows or cares
/// where the pixels came from.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class GlyphAtlas
{
    /// <summary>Glyphs per row and per column in the atlas texture.</summary>
    public const int Columns = 16;

    private GlyphAtlas(int cellWidth, int cellHeight, byte[] pixels)
    {
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Pixels = pixels;
        Width = cellWidth * Columns;
        Height = cellHeight * Columns;
    }

    public int CellWidth { get; }

    public int CellHeight { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Single-channel coverage, row-major, <see cref="Width"/> x <see cref="Height"/>.</summary>
    public byte[] Pixels { get; }

    /// <summary>
    /// Builds the atlas for a named bundled face. 8x16 is the VGA text cell.
    /// </summary>
    /// <remarks>
    /// A name that is not in the catalogue falls back to the default face rather than
    /// throwing: it arrives from a settings file, and a typo there should cost a message
    /// and not a window.
    /// </remarks>
    public static GlyphAtlas BuildFace(string? faceName) =>
        Build(font: HexFont.LoadEmbedded((FontCatalog.Find(faceName) ?? FontCatalog.Default).Resource));

    /// <summary>Builds the atlas. 8x16 is the VGA text cell and the default everywhere.</summary>
    public static GlyphAtlas Build(
        int cellWidth = 8,
        int cellHeight = 16,
        string? fontFamily = null,
        double threshold = 0,
        HexFont? font = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cellWidth, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellHeight, 6);

        var pixels = new byte[cellWidth * Columns * cellHeight * Columns];
        var atlas = new GlyphAtlas(cellWidth, cellHeight, pixels);

        // Order matters. The outline face is rasterised first to fill every slot, then the
        // bitmap font is blitted over the slots it covers. Whatever is left came from the
        // outline face, and gets thresholded to 1-bit so it does not sit next to crisp
        // bitmap glyphs looking blurry.
        atlas.RasterizeFromFont(fontFamily);

        var gaps = new List<int>();
        if (cellWidth == 8 && cellHeight == 16 && (font ?? HexFont.LoadEmbedded()) is { } bitmap)
        {
            gaps = atlas.BlitFrom(bitmap);
            gaps = atlas.DeriveMissing(bitmap, gaps);
        }

        foreach (var slot in gaps)
        {
            atlas.Threshold(slot, 0.45);
        }

        // A hard threshold everywhere turns the antialiasing into 1-bit coverage. Rarely
        // wanted now that the bitmap face covers almost everything, so it stays opt-in.
        if (threshold > 0)
        {
            for (var slot = 0; slot < 256; slot++)
            {
                atlas.Threshold(slot, threshold);
            }
        }

        atlas.Clear(0x00);   // NUL draws nothing
        atlas.Clear(0x20);   // and neither does space
        atlas.Clear(0xFF);   // nor non-breaking space

        return atlas;
    }

    private void Clear(int slot)
    {
        var (originX, originY) = Origin(slot);
        for (var y = 0; y < CellHeight; y++)
        {
            Array.Clear(Pixels, ((originY + y) * Width) + originX, CellWidth);
        }
    }

    private (int X, int Y) Origin(int slot) =>
        ((slot % Columns) * CellWidth, (slot / Columns) * CellHeight);

    private void Set(int slot, int x, int y, byte value)
    {
        if (x < 0 || x >= CellWidth || y < 0 || y >= CellHeight)
        {
            return;
        }

        var (originX, originY) = Origin(slot);
        Pixels[((originY + y) * Width) + originX + x] = value;
    }

    // ---- Rasterisation from a system face ------------------------------------

    private void RasterizeFromFont(string? requested)
    {
        var family = ResolveFamily(requested);

        // Render the sheet in one pass at 4x and box-filter down. Direct rasterisation at
        // eight pixels wide loses stems entirely on glyphs like 'l' and '1'; supersampling
        // keeps them as grey rather than dropping them.
        const int Supersample = 4;
        var cellW = CellWidth * Supersample;
        var cellH = CellHeight * Supersample;

        using var bitmap = new Bitmap(cellW * Columns, cellH * Columns, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var size = FitPointSize(graphics, family, cellW, cellH);
            using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            };

            for (var slot = 0; slot < 256; slot++)
            {
                var text = Cp437.ToUnicode[slot].ToString();
                var box = new RectangleF(
                    slot % Columns * cellW,
                    slot / Columns * cellH,
                    cellW,
                    cellH);

                graphics.DrawString(text, font, brush, box, format);
            }
        }

        Downsample(bitmap, Supersample);
    }

    private static FontFamily ResolveFamily(string? requested)
    {
        foreach (var name in new[] { requested, "Consolas", "Cascadia Mono", "Lucida Console", "Courier New" })
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                return new FontFamily(name);
            }
            catch (ArgumentException)
            {
                // Not installed; try the next.
            }
        }

        return FontFamily.GenericMonospace;
    }

    /// <summary>Largest pixel size whose 'M' still fits the cell, so nothing is clipped.</summary>
    private static float FitPointSize(Graphics graphics, FontFamily family, int cellW, int cellH)
    {
        var best = cellH * 0.8f;
        for (var size = (float)cellH; size >= 4f; size -= 0.5f)
        {
            using var candidate = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString("M", candidate, PointF.Empty, StringFormat.GenericTypographic);
            if (measured.Width <= cellW && measured.Height <= cellH)
            {
                best = size;
                break;
            }
        }

        return best;
    }

    private void Downsample(Bitmap source, int factor)
    {
        var data = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var area = factor * factor;
            unsafe
            {
                var basePtr = (byte*)data.Scan0;
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var total = 0;
                        for (var sy = 0; sy < factor; sy++)
                        {
                            var row = basePtr + (((y * factor) + sy) * data.Stride);
                            for (var sx = 0; sx < factor; sx++)
                            {
                                // Green channel: the text was drawn white on black, so any
                                // channel carries the coverage.
                                total += row[(((x * factor) + sx) * 4) + 1];
                            }
                        }

                        Pixels[(y * Width) + x] = (byte)(total / area);
                    }
                }
            }
        }
        finally
        {
            source.UnlockBits(data);
        }
    }

    /// <summary>
    /// Copies every glyph the bitmap font provides into its CP437 slot. Returns the slots
    /// it could not fill, which stay as the outline face rasterised them.
    /// </summary>
    /// <remarks>
    /// No geometric synthesis of box drawing or block elements any more. A font designed
    /// for an 8x16 cell already draws them so they join, and substituting our own would be
    /// a downgrade — the earlier synthesis never got double-line junctions quite right.
    /// </remarks>
    private List<int> BlitFrom(HexFont font)
    {
        var gaps = new List<int>();

        for (var slot = 0; slot < 256; slot++)
        {
            if (font.Rows(Cp437.ToUnicode[slot]) is not { } rows)
            {
                gaps.Add(slot);
                continue;
            }

            // Most significant bit is the leftmost pixel.
            Blit(slot, rows, mirror: false);
        }

        return gaps;
    }

    /// <summary>
    /// Fills what gaps it can from glyphs the font does have, rather than falling back to
    /// the outline face. Returns the slots still unfilled.
    /// </summary>
    private List<int> DeriveMissing(HexFont font, List<int> gaps)
    {
        var remaining = new List<int>();

        foreach (var slot in gaps)
        {
            switch (Cp437.ToUnicode[slot])
            {
                // BULLET OPERATOR is the same mark as BULLET at this size.
                case '∙' when font.Rows('•') is { } bullet:
                    Blit(slot, bullet, mirror: false);
                    break;

                // REVERSED NOT SIGN is NOT SIGN flipped left-to-right, so deriving it keeps
                // the two consistent instead of pairing a bitmap glyph with an outline one.
                case '⌐' when font.Rows('¬') is { } notSign:
                    Blit(slot, notSign, mirror: true);
                    break;

                default:
                    remaining.Add(slot);
                    break;
            }
        }

        return remaining;
    }

    private void Blit(int slot, byte[] rows, bool mirror)
    {
        for (var y = 0; y < CellHeight && y < rows.Length; y++)
        {
            var bits = rows[y];
            for (var x = 0; x < CellWidth; x++)
            {
                var source = mirror ? CellWidth - 1 - x : x;
                Set(slot, x, y, (bits & (0x80 >> source)) != 0 ? (byte)0xFF : (byte)0x00);
            }
        }
    }

    /// <summary>Collapses one slot's antialiasing to 1-bit coverage.</summary>
    private void Threshold(int slot, double level)
    {
        var cut = (byte)(Math.Clamp(level, 0, 1) * 255);
        var (originX, originY) = Origin(slot);

        for (var y = 0; y < CellHeight; y++)
        {
            for (var x = 0; x < CellWidth; x++)
            {
                var index = ((originY + y) * Width) + originX + x;
                Pixels[index] = Pixels[index] >= cut ? (byte)0xFF : (byte)0x00;
            }
        }
    }

    /// <summary>
    /// Writes the atlas as a greyscale PNG. <paramref name="magnify"/> enlarges by whole
    /// pixels so the actual bitmap is visible rather than a smoothed version of it.
    /// </summary>
    public void SaveDebugImage(string path, int magnify = 1)
    {
        magnify = Math.Max(1, magnify);
        using var bitmap = new Bitmap(Width * magnify, Height * magnify, PixelFormat.Format32bppArgb);

        for (var y = 0; y < Height * magnify; y++)
        {
            for (var x = 0; x < Width * magnify; x++)
            {
                var v = Pixels[((y / magnify) * Width) + (x / magnify)];
                bitmap.SetPixel(x, y, Color.FromArgb(255, v, v, v));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
