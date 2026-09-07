using System.Drawing;
using System.Drawing.Imaging;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Runtime.Versioning;
using Baudacious.Core.Terminal;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Baudacious.Render;

/// <summary>
/// Renders one frame through the real GL pipeline into a hidden window and hands back the
/// pixels.
/// </summary>
/// <remarks>
/// The only way to check a GPU renderer without looking at a screen. It uses the same
/// <see cref="TextRenderer"/>, the same shaders and the same atlas as the live window, so a
/// frame captured here is what the window would have shown — which makes glyph alignment,
/// box-drawing joins and the phosphor transform reviewable, and regressions in them
/// catchable.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class OffscreenRenderer
{
    public static void CaptureToPng(
        Screen screen,
        DisplaySettings settings,
        string path,
        int magnify = 1,
        string? status = null,
        IReadOnlyList<string>? overlay = null,
        int fadeFrames = 0)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(settings);

        var options = WindowOptions.Default with
        {
            Size = new Silk.NET.Maths.Vector2D<int>(64, 64),
            IsVisible = false,
            Title = "Baudacious offscreen",
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.Default,
                new APIVersion(3, 3)),
        };

        using var window = Window.Create(options);
        window.Initialize();

        var gl = GL.GetApi(window);
        var atlas = GlyphAtlas.Build();

        using var renderer = new TextRenderer(gl, atlas);
        renderer.RenderToTarget(screen, settings, TimeSpan.Zero, status, selection: null, overlay: overlay);

        // Capture what would reach the screen, not the text target: the CRT pass runs at
        // output resolution, so a frame read back before it would show none of it.
        var width = renderer.TargetWidth * Math.Max(1, magnify);
        var height = renderer.TargetHeight * Math.Max(1, magnify);
        var (framebuffer, texture) = CreateOutputTarget(gl, width, height);

        renderer.Present(width, height, settings, framebuffer);

        // Persistence is the one effect that exists only across frames, so a single render
        // can never show it. Clear the screen and keep presenting through the same pass:
        // what is left in the frame afterwards is the tail, and how much is left after N
        // frames is the whole question.
        if (fadeFrames > 0)
        {
            var blank = new Screen(screen.Columns, screen.RowCount) { CursorVisible = false };

            for (var frame = 0; frame < fadeFrames; frame++)
            {
                renderer.RenderToTarget(blank, settings, TimeSpan.Zero, status);
                renderer.Present(width, height, settings, framebuffer);
            }
        }

        var pixels = ReadBack(gl, framebuffer, width, height);
        gl.DeleteFramebuffer(framebuffer);
        gl.DeleteTexture(texture);

        Save(pixels, width, height, path, magnify: 1);

        window.Close();
    }

    /// <summary>
    /// Times a frame through the real pipeline, with and without the CRT pass.
    /// </summary>
    /// <remarks>
    /// Uses a hidden window and a finish after each frame, so the number is the GPU's, not
    /// the driver's queueing. Answers two questions the specs ask: what the CRT pass costs,
    /// and whether a fully populated grid at the highest rate still fits in a frame.
    /// </remarks>
    public static (double CrtMs, double PlainMs) Benchmark(
        Screen screen, DisplaySettings settings, int magnify, int frames)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(settings);

        var options = WindowOptions.Default with
        {
            Size = new Silk.NET.Maths.Vector2D<int>(64, 64),
            IsVisible = false,
            Title = "Baudacious benchmark",
            API = new GraphicsAPI(
                ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3)),
        };

        using var window = Window.Create(options);
        window.Initialize();

        var gl = GL.GetApi(window);
        var atlas = GlyphAtlas.Build();
        using var renderer = new TextRenderer(gl, atlas);

        renderer.RenderToTarget(screen, settings, TimeSpan.Zero);
        var width = renderer.TargetWidth * magnify;
        var height = renderer.TargetHeight * magnify;
        var (framebuffer, texture) = CreateOutputTarget(gl, width, height);

        double Time(DisplaySettings s)
        {
            // Warm up: first frames pay for shader and pipeline-state compilation.
            for (var i = 0; i < 20; i++)
            {
                renderer.RenderToTarget(screen, s, TimeSpan.Zero);
                renderer.Present(width, height, s, framebuffer);
            }

            gl.Finish();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < frames; i++)
            {
                renderer.RenderToTarget(screen, s, TimeSpan.Zero);
                renderer.Present(width, height, s, framebuffer);
            }

            gl.Finish();
            return clock.Elapsed.TotalMilliseconds / frames;
        }

        var crt = Time(settings with { Crt = settings.Crt with { Enabled = true } });
        var plain = Time(settings with { Crt = CrtSettings.Off });

        gl.DeleteFramebuffer(framebuffer);
        gl.DeleteTexture(texture);
        window.Close();

        return (crt, plain);
    }

    private static unsafe (uint Framebuffer, uint Texture) CreateOutputTarget(GL gl, int width, int height)
    {
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(
            TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)width, (uint)height, 0,
            Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

        var framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, texture, 0);

        var state = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (state != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"capture target incomplete: {state}");
        }

        return (framebuffer, texture);
    }

    private static unsafe byte[] ReadBack(GL gl, uint framebuffer, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        fixed (byte* p = pixels)
        {
            gl.ReadPixels(
                0, 0, (uint)width, (uint)height,
                Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }

        // OpenGL hands back bottom-up.
        var stride = width * 4;
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(pixels, (height - 1 - y) * stride, flipped, y * stride, stride);
        }

        return flipped;
    }

    private static void Save(byte[] rgba, int width, int height, string path, int magnify)
    {
        magnify = Math.Max(1, magnify);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = new Bitmap(width * magnify, height * magnify, GdiPixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            GdiPixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                var target = (byte*)data.Scan0;
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var sourceRow = (y / magnify) * width * 4;
                    var targetRow = target + (y * data.Stride);
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var s = sourceRow + ((x / magnify) * 4);

                        // GDI wants BGRA; the framebuffer gave us RGBA.
                        targetRow[(x * 4) + 0] = rgba[s + 2];
                        targetRow[(x * 4) + 1] = rgba[s + 1];
                        targetRow[(x * 4) + 2] = rgba[s + 0];
                        targetRow[(x * 4) + 3] = 255;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
