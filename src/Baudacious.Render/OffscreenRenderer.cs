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
    public static void CaptureToPng(Screen screen, DisplaySettings settings, string path, int magnify = 1)
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
        renderer.RenderToTarget(screen, settings, TimeSpan.Zero);

        var pixels = renderer.ReadTarget();
        Save(pixels, renderer.TargetWidth, renderer.TargetHeight, path, magnify);

        window.Close();
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
