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
/// Letters and punctuation are rasterised from a system monospace face. Box drawing,
/// block elements and shading are <b>synthesised geometrically instead</b>, because a
/// typeface rendered at eight pixels wide will not make <c>┌</c> and <c>─</c> meet
/// exactly, and a table with visible seams at every join looks broken. Drawing the runs
/// ourselves guarantees they line up.
/// </para>
/// <para>
/// This is the pragmatic stand-in for the authentic IBM VGA ROM face, which is Open
/// Question 1 in design.md. Nothing above this class knows where the pixels came from, so
/// resolving that question means replacing this one file's raster source.
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

    /// <summary>Builds the atlas. 8x16 is the VGA text cell and the default everywhere.</summary>
    public static GlyphAtlas Build(int cellWidth = 8, int cellHeight = 16, string? fontFamily = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cellWidth, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellHeight, 6);

        var pixels = new byte[cellWidth * Columns * cellHeight * Columns];
        var atlas = new GlyphAtlas(cellWidth, cellHeight, pixels);

        atlas.RasterizeFromFont(fontFamily);
        atlas.SynthesizeBlocks();
        atlas.SynthesizeBoxDrawing();
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

    private void FillRect(int slot, int x0, int y0, int x1, int y1, byte value = 0xFF)
    {
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                Set(slot, x, y, value);
            }
        }
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

    // ---- Synthesised block elements ------------------------------------------

    private void SynthesizeBlocks()
    {
        var right = CellWidth - 1;
        var bottom = CellHeight - 1;
        var midX = (CellWidth / 2) - 1;
        var midY = (CellHeight / 2) - 1;

        Clear(0xDB);
        FillRect(0xDB, 0, 0, right, bottom);                    // █ full

        Clear(0xDC);
        FillRect(0xDC, 0, midY + 1, right, bottom);             // ▄ lower half

        Clear(0xDD);
        FillRect(0xDD, 0, 0, midX, bottom);                     // ▌ left half

        Clear(0xDE);
        FillRect(0xDE, midX + 1, 0, right, bottom);             // ▐ right half

        Clear(0xDF);
        FillRect(0xDF, 0, 0, right, midY);                      // ▀ upper half

        // The shades are dither patterns rather than flat greys, which is what the ROM
        // font did and what makes them read as texture at 1x.
        Dither(0xB0, (x, y) => (x % 2 == 0) && (y % 2 == 0));                   // ░ 25%
        Dither(0xB1, (x, y) => (x + y) % 2 == 0);                               // ▒ 50%
        Dither(0xB2, (x, y) => !((x % 2 == 1) && (y % 2 == 1)));                // ▓ 75%

        // ■ and ∙ and · are small centred blocks; the font's versions are inconsistent.
        Clear(0xFE);
        FillRect(0xFE, 1, 3, CellWidth - 2, CellHeight - 5);                     // ■
    }

    private void Dither(int slot, Func<int, int, bool> on)
    {
        Clear(slot);
        for (var y = 0; y < CellHeight; y++)
        {
            for (var x = 0; x < CellWidth; x++)
            {
                if (on(x, y))
                {
                    Set(slot, x, y, 0xFF);
                }
            }
        }
    }

    // ---- Synthesised box drawing ----------------------------------------------

    /// <summary>Line weight on each side of a box-drawing glyph: 0 none, 1 single, 2 double.</summary>
    private readonly record struct Box(int Up, int Down, int Left, int Right);

    private static readonly Dictionary<int, Box> BoxTable = new()
    {
        [0xB3] = new(1, 1, 0, 0), [0xB4] = new(1, 1, 1, 0), [0xB5] = new(1, 1, 2, 0),
        [0xB6] = new(2, 2, 1, 0), [0xB7] = new(0, 2, 1, 0), [0xB8] = new(0, 1, 2, 0),
        [0xB9] = new(2, 2, 2, 0), [0xBA] = new(2, 2, 0, 0), [0xBB] = new(0, 2, 2, 0),
        [0xBC] = new(2, 0, 2, 0), [0xBD] = new(2, 0, 1, 0), [0xBE] = new(1, 0, 2, 0),
        [0xBF] = new(0, 1, 1, 0), [0xC0] = new(1, 0, 0, 1), [0xC1] = new(1, 0, 1, 1),
        [0xC2] = new(0, 1, 1, 1), [0xC3] = new(1, 1, 0, 1), [0xC4] = new(0, 0, 1, 1),
        [0xC5] = new(1, 1, 1, 1), [0xC6] = new(1, 1, 0, 2), [0xC7] = new(2, 2, 0, 1),
        [0xC8] = new(2, 0, 0, 2), [0xC9] = new(0, 2, 0, 2), [0xCA] = new(2, 0, 2, 2),
        [0xCB] = new(0, 2, 2, 2), [0xCC] = new(2, 2, 0, 2), [0xCD] = new(0, 0, 2, 2),
        [0xCE] = new(2, 2, 2, 2), [0xCF] = new(1, 0, 2, 2), [0xD0] = new(2, 0, 1, 1),
        [0xD1] = new(0, 1, 2, 2), [0xD2] = new(0, 2, 1, 1), [0xD3] = new(2, 0, 0, 1),
        [0xD4] = new(1, 0, 0, 2), [0xD5] = new(0, 1, 0, 2), [0xD6] = new(0, 2, 0, 1),
        [0xD7] = new(2, 2, 1, 1), [0xD8] = new(1, 1, 2, 2), [0xD9] = new(1, 0, 1, 0),
        [0xDA] = new(0, 1, 0, 1),
    };

    private void SynthesizeBoxDrawing()
    {
        // Single lines sit on the centre row and column; double lines straddle them. Every
        // glyph is built from the same four coordinates, which is what makes the joins land
        // exactly no matter which pair of characters end up side by side.
        var cx = (CellWidth - 1) / 2;
        var cy = (CellHeight - 1) / 2;
        var stroke = Math.Max(1, CellHeight / 16);
        var cxA = cx - stroke;
        var cxB = cx + stroke;
        var cyA = cy - stroke;
        var cyB = cy + stroke;
        var right = CellWidth - 1;
        var bottom = CellHeight - 1;

        foreach (var (slot, box) in BoxTable)
        {
            Clear(slot);

            // How far an arm runs is decided by the member it meets, not by its own weight:
            // a horizontal arm stops where the vertical stroke begins. With no perpendicular
            // member at all it crosses the whole cell, which is what makes a bare - or |
            // continuous with its neighbours.
            var vertical = Math.Max(box.Up, box.Down);
            var horizontal = Math.Max(box.Left, box.Right);

            var leftStop = vertical == 0 ? right : vertical == 1 ? cx + stroke - 1 : cxA + stroke - 1;
            var rightStart = vertical == 0 ? 0 : vertical == 1 ? cx : cxB;
            var upStop = horizontal == 0 ? bottom : horizontal == 1 ? cy + stroke - 1 : cyA + stroke - 1;
            var downStart = horizontal == 0 ? 0 : horizontal == 1 ? cy : cyB;

            DrawArm(slot, box.Left, horizontal: true, from: 0, to: leftStop, cyA, cy, cyB);
            DrawArm(slot, box.Right, horizontal: true, from: rightStart, to: right, cyA, cy, cyB);
            DrawArm(slot, box.Up, horizontal: false, from: 0, to: upStop, cxA, cx, cxB);
            DrawArm(slot, box.Down, horizontal: false, from: downStart, to: bottom, cxA, cx, cxB);
        }
    }

    private void DrawArm(int slot, int weight, bool horizontal, int from, int to, int lowLane, int midLane, int highLane)
    {
        if (weight == 0 || to < from)
        {
            return;
        }

        // Stroke width tracks the cell so a larger atlas is not drawn in hairlines. The
        // 8x16 VGA cell gives 1, which is what the ROM font used.
        var stroke = Math.Max(1, CellHeight / 16);
        Span<int> lanes = weight == 1 ? [midLane] : [lowLane, highLane];

        foreach (var lane in lanes)
        {
            for (var t = 0; t < stroke; t++)
            {
                for (var i = from; i <= to; i++)
                {
                    if (horizontal)
                    {
                        Set(slot, i, lane + t, 0xFF);
                    }
                    else
                    {
                        Set(slot, lane + t, i, 0xFF);
                    }
                }
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
