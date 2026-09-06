using System.Runtime.InteropServices;
using Baudacious.Core.Terminal;
using Silk.NET.OpenGL;

namespace Baudacious.Render;

/// <summary>
/// Draws a <see cref="Screen"/> as instanced textured quads, one per cell.
/// </summary>
/// <remarks>
/// <para>
/// Two passes. The first draws text into an offscreen target at <b>native cell
/// resolution</b> — one texel per glyph pixel, no scaling. The second blits that target to
/// the window with nearest sampling and whole-number magnification.
/// </para>
/// <para>
/// Rendering at native resolution first is what makes the CRT pass of Phase 5 possible at
/// all: scanlines, bloom radius and phosphor bleed are properties of the <em>CRT's</em>
/// pixel grid, not the LCD's. Defining them in this target's space and magnifying
/// afterwards is the difference between looking like a CRT and looking like a filter
/// applied to a screenshot.
/// </para>
/// </remarks>
public sealed class TextRenderer : IDisposable
{
    private const int MaxInstances = 200 * 80;

    private readonly GL _gl;
    private readonly GlyphAtlas _atlas;
    private readonly CrtPass _crt;

    private uint _program;
    private uint _blitProgram;
    private uint _vao;
    private uint _quadBuffer;
    private uint _instanceBuffer;
    private uint _glyphTexture;
    private uint _framebuffer;
    private uint _colorTarget;
    private uint _blitVao;

    private Instance[] _instances = new Instance[MaxInstances];
    private int _instanceCount;

    private int _targetWidth;
    private int _targetHeight;

    public TextRenderer(GL gl, GlyphAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(atlas);

        _gl = gl;
        _atlas = atlas;

        CreatePrograms();
        CreateGeometry();
        CreateGlyphTexture();
        _crt = new CrtPass(gl);
    }

    /// <summary>Width in pixels of the offscreen target — the CRT's own resolution.</summary>
    public int TargetWidth => _targetWidth;

    public int TargetHeight => _targetHeight;

    /// <summary>One cell's worth of instance data. Sixteen bytes, matching the shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Instance
    {
        public uint Position;     // cellX in the high half, cellY in the low half
        public uint Foreground;   // RGBA
        public uint Background;   // RGBA
        public uint GlyphAndFlags;
    }

    [Flags]
    private enum DrawFlags : uint
    {
        None = 0,
        Underline = 1 << 0,
        Strikethrough = 1 << 1,
        CursorBlock = 1 << 2,
        CursorUnderline = 1 << 3,
        CursorBar = 1 << 4,
    }

    // ---- Setup ---------------------------------------------------------------

    private const string TextVertexShader = """
        #version 330 core
        layout(location = 0) in vec2 aCorner;
        layout(location = 1) in uint aPosition;
        layout(location = 2) in uint aForeground;
        layout(location = 3) in uint aBackground;
        layout(location = 4) in uint aGlyphAndFlags;

        uniform vec2 uGridSize;      // columns, rows
        uniform vec2 uCellSize;      // glyph width, height in texels
        uniform vec2 uAtlasSize;     // atlas texture size in texels

        out vec2 vAtlasUv;
        out vec2 vCellUv;
        flat out vec4 vForeground;
        flat out vec4 vBackground;
        flat out uint vFlags;

        vec4 unpackRgba(uint value) {
            return vec4(
                float((value >> 24) & 0xFFu),
                float((value >> 16) & 0xFFu),
                float((value >> 8) & 0xFFu),
                float(value & 0xFFu)) / 255.0;
        }

        void main() {
            float cellX = float(aPosition >> 16);
            float cellY = float(aPosition & 0xFFFFu);

            // Cell space to clip space, with y running downwards as the grid does.
            vec2 pixel = (vec2(cellX, cellY) + aCorner) * uCellSize;
            vec2 ndc = (pixel / (uGridSize * uCellSize)) * 2.0 - 1.0;
            gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);

            uint glyph = aGlyphAndFlags >> 16;
            vec2 glyphOrigin = vec2(float(glyph % 16u), float(glyph / 16u)) * uCellSize;
            vAtlasUv = (glyphOrigin + aCorner * uCellSize) / uAtlasSize;

            vCellUv = aCorner;
            vForeground = unpackRgba(aForeground);
            vBackground = unpackRgba(aBackground);
            vFlags = aGlyphAndFlags & 0xFFFFu;
        }
        """;

    private const string TextFragmentShader = """
        #version 330 core
        in vec2 vAtlasUv;
        in vec2 vCellUv;
        flat in vec4 vForeground;
        flat in vec4 vBackground;
        flat in uint vFlags;

        uniform sampler2D uGlyphs;
        uniform vec2 uCellSize;

        out vec4 fragColor;

        const uint FLAG_UNDERLINE      = 1u;
        const uint FLAG_STRIKETHROUGH  = 2u;
        const uint FLAG_CURSOR_BLOCK   = 4u;
        const uint FLAG_CURSOR_UNDER   = 8u;
        const uint FLAG_CURSOR_BAR     = 16u;

        void main() {
            float coverage = texture(uGlyphs, vAtlasUv).r;

            // Rules drawn in cell space so they scale with the cell rather than the window.
            float row = vCellUv.y * uCellSize.y;
            if ((vFlags & FLAG_UNDERLINE) != 0u && row >= uCellSize.y - 2.0) {
                coverage = 1.0;
            }
            if ((vFlags & FLAG_STRIKETHROUGH) != 0u &&
                abs(row - uCellSize.y * 0.5) < 1.0) {
                coverage = 1.0;
            }

            vec4 color = mix(vBackground, vForeground, coverage);

            if ((vFlags & FLAG_CURSOR_BLOCK) != 0u) {
                color = vec4(vForeground.rgb, 1.0) * (1.0 - coverage) + vBackground * coverage;
            } else if ((vFlags & FLAG_CURSOR_UNDER) != 0u && row >= uCellSize.y - 2.0) {
                color = vForeground;
            } else if ((vFlags & FLAG_CURSOR_BAR) != 0u && vCellUv.x * uCellSize.x < 2.0) {
                color = vForeground;
            }

            fragColor = vec4(color.rgb, 1.0);
        }
        """;

    private const string BlitVertexShader = """
        #version 330 core
        out vec2 vUv;
        void main() {
            // Full-screen triangle; no vertex buffer needed.
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string BlitFragmentShader = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uSource;
        out vec4 fragColor;
        void main() {
            // No flip. Row 0 of the grid was drawn at the top of the offscreen target in
            // GL's bottom-up convention, which is texture v = 1 — the same place the top of
            // the screen samples from. Flipping here turns the display upside down, and the
            // screenshot path hides it because ReadTarget flips separately.
            fragColor = texture(uSource, vUv);
        }
        """;

    private void CreatePrograms()
    {
        _program = Link(TextVertexShader, TextFragmentShader);
        _blitProgram = Link(BlitVertexShader, BlitFragmentShader);
    }

    private uint Link(string vertexSource, string fragmentSource)
    {
        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);

        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            throw new InvalidOperationException($"shader link failed: {_gl.GetProgramInfoLog(program)}");
        }

        _gl.DetachShader(program, vertex);
        _gl.DetachShader(program, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        return program;
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            throw new InvalidOperationException($"{type} failed to compile: {_gl.GetShaderInfoLog(shader)}");
        }

        return shader;
    }

    private unsafe void CreateGeometry()
    {
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        // Unit quad, two triangles.
        float[] corners = [0f, 0f, 1f, 0f, 0f, 1f, 1f, 0f, 1f, 1f, 0f, 1f];
        _quadBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadBuffer);
        fixed (float* p = corners)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(corners.Length * sizeof(float)),
                p,
                BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);

        _instanceBuffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer);
        _gl.BufferData(
            BufferTargetARB.ArrayBuffer,
            (nuint)(MaxInstances * sizeof(Instance)),
            null,
            BufferUsageARB.StreamDraw);

        var stride = (uint)sizeof(Instance);
        for (uint i = 0; i < 4; i++)
        {
            _gl.EnableVertexAttribArray(1 + i);
            _gl.VertexAttribIPointer(1 + i, 1, VertexAttribIType.UnsignedInt, stride, (void*)(i * sizeof(uint)));
            _gl.VertexAttribDivisor(1 + i, 1);
        }

        _gl.BindVertexArray(0);

        _blitVao = _gl.GenVertexArray();
    }

    private unsafe void CreateGlyphTexture()
    {
        _glyphTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _glyphTexture);

        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* pixels = _atlas.Pixels)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.R8,
                (uint)_atlas.Width,
                (uint)_atlas.Height,
                0,
                PixelFormat.Red,
                PixelType.UnsignedByte,
                pixels);
        }

        // Nearest everywhere: a glyph pixel is a glyph pixel. Any filtering here would blur
        // the thing the CRT pass is supposed to blur deliberately.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    // ---- Target --------------------------------------------------------------

    /// <summary>Sizes the offscreen target to the grid, at one texel per glyph pixel.</summary>
    public unsafe void ResizeTarget(int columns, int rows)
    {
        // `rows` here already includes the status line when one is shown.
        var width = columns * _atlas.CellWidth;
        var height = rows * _atlas.CellHeight;

        if (width == _targetWidth && height == _targetHeight && _framebuffer != 0)
        {
            return;
        }

        if (_framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_framebuffer);
            _gl.DeleteTexture(_colorTarget);
        }

        _targetWidth = width;
        _targetHeight = height;

        _colorTarget = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _colorTarget);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            null);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _colorTarget,
            0);

        var state = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (state != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"offscreen target incomplete: {state}");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // ---- Drawing --------------------------------------------------------------

    /// <summary>Draws the grid into the offscreen target.</summary>
    public unsafe void RenderToTarget(
        Screen screen,
        DisplaySettings settings,
        TimeSpan elapsed,
        string? status = null,
        Selection? selection = null,
        IReadOnlyList<string>? overlay = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(settings);

        var totalRows = screen.RowCount + (status is null ? 0 : 1);
        ResizeTarget(screen.Columns, totalRows);
        BuildInstances(screen, settings, elapsed, status, selection, overlay);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)_targetWidth, (uint)_targetHeight);

        var (r, g, b) = BackgroundOf(settings);
        _gl.ClearColor(r, g, b, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        if (_instanceCount == 0)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            return;
        }

        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer);
        fixed (Instance* p = _instances)
        {
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                0,
                (nuint)(_instanceCount * sizeof(Instance)),
                p);
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _glyphTexture);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uGlyphs"), 0);
        _gl.Uniform2(_gl.GetUniformLocation(_program, "uGridSize"), (float)screen.Columns, (float)totalRows);
        _gl.Uniform2(_gl.GetUniformLocation(_program, "uCellSize"), (float)_atlas.CellWidth, (float)_atlas.CellHeight);
        _gl.Uniform2(_gl.GetUniformLocation(_program, "uAtlasSize"), (float)_atlas.Width, (float)_atlas.Height);

        _gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, (uint)_instanceCount);

        _gl.BindVertexArray(0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Blits the offscreen target to the current framebuffer, centred, at the largest whole
    /// magnification that fits.
    /// </summary>
    /// <remarks>
    /// Whole numbers only. A fractional scale would resample glyph pixels unevenly and make
    /// some stems a pixel wider than others, which at 8x16 is the difference between crisp
    /// and mushy.
    /// </remarks>
    public void Present(int windowWidth, int windowHeight, DisplaySettings settings) =>
        Present(windowWidth, windowHeight, settings, destinationFramebuffer: 0);

    /// <summary>
    /// Puts the offscreen target on screen, centred, at the largest whole magnification
    /// that fits, through the CRT pass unless it is switched off.
    /// </summary>
    /// <remarks>
    /// Whole-number magnification only. A fractional scale resamples glyph pixels unevenly
    /// and makes some stems a pixel wider than others, which at 8x16 is the difference
    /// between crisp and mushy — and it would put the scanline period out of step with the
    /// pixel grid it is supposed to describe.
    /// </remarks>
    public void Present(
        int windowWidth, int windowHeight, DisplaySettings settings, uint destinationFramebuffer)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFramebuffer);

        var (r, g, b) = BackgroundOf(settings);
        _gl.Viewport(0, 0, (uint)windowWidth, (uint)windowHeight);
        _gl.ClearColor(r * 0.4f, g * 0.4f, b * 0.4f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        if (_targetWidth == 0 || _targetHeight == 0)
        {
            return;
        }

        var scale = Math.Max(1, Math.Min(windowWidth / _targetWidth, windowHeight / _targetHeight));
        var drawWidth = _targetWidth * scale;
        var drawHeight = _targetHeight * scale;
        var originX = (windowWidth - drawWidth) / 2;
        var originY = (windowHeight - drawHeight) / 2;

        if (settings.Crt.Enabled)
        {
            _crt.Render(
                _colorTarget, _targetWidth, _targetHeight,
                destinationFramebuffer,
                originX, originY, drawWidth, drawHeight,
                settings.Crt, settings.Phosphor);
            return;
        }

        // Bypass: a straight nearest blit, skipping six draws and four intermediate
        // targets. This is the reference for what the CRT pass costs.
        _gl.Viewport(originX, originY, (uint)drawWidth, (uint)drawHeight);
        _gl.UseProgram(_blitProgram);
        _gl.BindVertexArray(_blitVao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _colorTarget);
        _gl.Uniform1(_gl.GetUniformLocation(_blitProgram, "uSource"), 0);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
    }

    private static (float R, float G, float B) BackgroundOf(DisplaySettings settings)
    {
        if (settings.Phosphor == Phosphor.Color)
        {
            return (0f, 0f, 0f);
        }

        // Not pure black: an unlit phosphor still has a faint cast from ambient light.
        var (r, g, b) = PhosphorMap.Tint(settings.Phosphor);
        return ((float)(r * 0.02), (float)(g * 0.02), (float)(b * 0.02));
    }

    private void BuildInstances(
        Screen screen,
        DisplaySettings settings,
        TimeSpan elapsed,
        string? status,
        Selection? selection,
        IReadOnlyList<string>? overlay)
    {
        var needed = (screen.Columns * (screen.RowCount + 1)) + (screen.Columns * screen.RowCount);
        if (_instances.Length < needed)
        {
            _instances = new Instance[needed];
        }

        var textBlinkOn = !settings.TextBlinkInterval.Equals(TimeSpan.Zero)
            && (long)(elapsed.TotalMilliseconds / settings.TextBlinkInterval.TotalMilliseconds) % 2 == 0;

        var cursorOn = screen.CursorVisible
            && !screen.IsScrolledBack
            && (!settings.CursorBlink
                || (long)(elapsed.TotalMilliseconds / settings.CursorBlinkInterval.TotalMilliseconds) % 2 == 0);

        var count = 0;
        for (var row = 0; row < screen.RowCount; row++)
        {
            for (var column = 0; column < screen.Columns; column++)
            {
                var cell = screen.GetViewCell(row, column);
                var (foreground, background) = PhosphorMap.Resolve(cell, settings.Phosphor);

                // Selected cells are shown reversed. Done here rather than as a cell flag
                // so that selecting text never touches what the emulator wrote.
                if (selection is not null && selection.Contains(screen.AbsoluteRowOf(row), column))
                {
                    (foreground, background) = (background, foreground);
                }

                var glyph = Cp437.IndexOf(cell.Rune);
                if (cell.Flags.HasFlag(CellFlags.Blink) && !textBlinkOn)
                {
                    glyph = 0;
                }

                var flags = DrawFlags.None;
                if (cell.Flags.HasFlag(CellFlags.Underline))
                {
                    flags |= DrawFlags.Underline;
                }

                if (cell.Flags.HasFlag(CellFlags.Strikethrough))
                {
                    flags |= DrawFlags.Strikethrough;
                }

                if (cursorOn && row == screen.CursorRow && column == screen.CursorColumn)
                {
                    flags |= settings.CursorShape switch
                    {
                        CursorShape.Underline => DrawFlags.CursorUnderline,
                        CursorShape.Bar => DrawFlags.CursorBar,
                        _ => DrawFlags.CursorBlock,
                    };
                }

                _instances[count++] = new Instance
                {
                    Position = ((uint)column << 16) | (uint)row,
                    Foreground = foreground,
                    Background = background,
                    GlyphAndFlags = ((uint)glyph << 16) | (uint)flags,
                };
            }
        }

        if (overlay is not null)
        {
            count = AppendOverlay(overlay, screen, settings, count);
        }

        if (status is not null)
        {
            count = AppendStatusRow(status, screen.Columns, screen.RowCount, settings, count);
        }

        _instanceCount = count;
    }

    /// <summary>
    /// Draws a panel of text over the grid, centred, on a dimmed ground.
    /// </summary>
    /// <remarks>
    /// Later instances win because they are drawn over earlier ones, so the overlay simply
    /// re-emits the cells it covers. No compositing and no separate pass.
    /// </remarks>
    private int AppendOverlay(
        IReadOnlyList<string> lines, Screen screen, DisplaySettings settings, int count)
    {
        var width = Math.Min(screen.Columns - 2, Math.Max(30, lines.Max(l => l.Length) + 2));
        var height = Math.Min(screen.RowCount - 2, lines.Count + 2);
        var left = (screen.Columns - width) / 2;
        var top = (screen.RowCount - height) / 2;

        var lit = PhosphorMap.Resolve(
            new Cell(' ', Palette.DefaultForeground, Palette.DefaultBackground, CellFlags.None),
            settings.Phosphor);

        var ground = Dim(lit.Foreground, 0.12);

        for (var row = 0; row < height; row++)
        {
            var text = row is 0 || row > lines.Count ? string.Empty : lines[row - 1];

            for (var column = 0; column < width; column++)
            {
                var character = column < text.Length ? text[column] : ' ';
                var edge = row == 0 || row == height - 1 || column == 0 || column == width - 1;

                if (_instances.Length <= count)
                {
                    return count;
                }

                _instances[count++] = new Instance
                {
                    Position = ((uint)(left + column) << 16) | (uint)(top + row),
                    Foreground = edge ? Dim(lit.Foreground, 0.55) : lit.Foreground,
                    Background = ground,
                    GlyphAndFlags = (uint)Cp437.IndexOf(character) << 16,
                };
            }
        }

        return count;
    }

    private static uint Dim(uint rgba, double factor) =>
        Palette.Rgb(
            (int)(((rgba >> 24) & 0xFF) * factor),
            (int)(((rgba >> 16) & 0xFF) * factor),
            (int)(((rgba >> 8) & 0xFF) * factor));

    /// <summary>
    /// Draws the status bar as one extra row below the grid, in reverse video.
    /// </summary>
    /// <remarks>
    /// It is deliberately not part of the terminal grid: the shell must never be able to
    /// scroll it away, overwrite it, or be told the screen is a row taller than it is.
    /// </remarks>
    private int AppendStatusRow(
        string status, int columns, int row, DisplaySettings settings, int count)
    {
        var lit = PhosphorMap.Resolve(
            new Cell(' ', Palette.DefaultForeground, Palette.DefaultBackground, CellFlags.None),
            settings.Phosphor);

        // Reverse video: the bar reads as chrome rather than as output.
        var foreground = lit.Background;
        var background = lit.Foreground;

        for (var column = 0; column < columns; column++)
        {
            var character = column < status.Length ? status[column] : ' ';

            _instances[count++] = new Instance
            {
                Position = ((uint)column << 16) | (uint)row,
                Foreground = foreground,
                Background = background,
                GlyphAndFlags = (uint)Cp437.IndexOf(character) << 16,
            };
        }

        return count;
    }

    /// <summary>Reads the offscreen target back as RGBA, top row first. For screenshots.</summary>
    public unsafe byte[] ReadTarget()
    {
        var pixels = new byte[_targetWidth * _targetHeight * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        fixed (byte* p = pixels)
        {
            _gl.ReadPixels(
                0, 0, (uint)_targetWidth, (uint)_targetHeight,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        // OpenGL hands back bottom-up; flip so row 0 is the top line of the grid.
        var stride = _targetWidth * 4;
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < _targetHeight; y++)
        {
            Array.Copy(pixels, (_targetHeight - 1 - y) * stride, flipped, y * stride, stride);
        }

        return flipped;
    }

    public void Dispose()
    {
        if (_framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_framebuffer);
            _gl.DeleteTexture(_colorTarget);
        }

        _gl.DeleteTexture(_glyphTexture);
        _gl.DeleteBuffer(_quadBuffer);
        _gl.DeleteBuffer(_instanceBuffer);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteVertexArray(_blitVao);
        _gl.DeleteProgram(_program);
        _crt.Dispose();
        _gl.DeleteProgram(_blitProgram);
    }
}
