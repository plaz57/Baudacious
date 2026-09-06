using Silk.NET.OpenGL;

namespace Baudacious.Render;

/// <summary>
/// The post-process that turns a crisp text image into something that looks like it came
/// off a phosphor tube: bloom, scanlines and vignette.
/// </summary>
/// <remarks>
/// <para>
/// Runs over the offscreen text target rather than replacing it, which is why the text
/// pipeline needed no changes to gain this. Bloom is built by downsampling to half and
/// quarter resolution and blurring each separably, then adding both back — cheaper and
/// softer than one wide blur at full size.
/// </para>
/// <para>
/// The composite runs at <b>output</b> resolution while its scanline phase is derived from
/// the <b>source</b> row, so the raster period stays tied to the emulated pixel grid while
/// the falloff is drawn as smoothly as the real display allows. That is the distinction
/// between looking like a CRT and looking like a filter over a screenshot.
/// </para>
/// <para>
/// <see cref="Render"/> takes its destination framebuffer as a parameter. Phosphor
/// persistence, when it arrives, needs to render into a retained target and read the
/// previous one — with the destination already parameterised, that is an added pass rather
/// than a restructuring.
/// </para>
/// </remarks>
public sealed class CrtPass : IDisposable
{
    private readonly GL _gl;

    private uint _blurProgram;
    private uint _compositeProgram;
    private uint _vao;

    private Target _half;
    private Target _halfScratch;
    private Target _quarter;
    private Target _quarterScratch;

    private int _sourceWidth;
    private int _sourceHeight;

    private readonly record struct Target(uint Framebuffer, uint Texture, int Width, int Height);

    public CrtPass(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;

        _blurProgram = Link(FullScreenVertexShader, BlurFragmentShader);
        _compositeProgram = Link(FullScreenVertexShader, CompositeFragmentShader);
        _vao = _gl.GenVertexArray();
    }

    // ---- Shaders ---------------------------------------------------------------

    private const string FullScreenVertexShader = """
        #version 330 core
        out vec2 vUv;
        void main() {
            // One oversized triangle covering the viewport; no vertex buffer needed.
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string BlurFragmentShader = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uSource;
        uniform vec2 uStep;          // one texel along the axis being blurred
        out vec4 fragColor;

        void main() {
            // Nine-tap Gaussian, run once per axis. Separable, so two passes cost what a
            // 9x9 kernel would cost in eighteen taps instead of eighty-one.
            float weight[5] = float[](0.227027, 0.194595, 0.121622, 0.054054, 0.016216);

            vec3 sum = texture(uSource, vUv).rgb * weight[0];
            for (int i = 1; i < 5; i++) {
                vec2 offset = uStep * float(i);
                sum += texture(uSource, vUv + offset).rgb * weight[i];
                sum += texture(uSource, vUv - offset).rgb * weight[i];
            }

            fragColor = vec4(sum, 1.0);
        }
        """;

    private const string CompositeFragmentShader = """
        #version 330 core
        in vec2 vUv;

        uniform sampler2D uText;
        uniform sampler2D uBloomHalf;
        uniform sampler2D uBloomQuarter;

        uniform vec2  uSourceSize;   // the text target, in texels
        uniform float uBloom;
        uniform float uScanline;
        uniform float uVignette;
        uniform float uAmbient;
        uniform vec3  uTint;

        out vec4 fragColor;

        void main() {
            vec3 base  = texture(uText, vUv).rgb;
            vec3 bloom = texture(uBloomHalf, vUv).rgb * 0.6
                       + texture(uBloomQuarter, vUv).rgb * 0.4;

            vec3 color = base + bloom * uBloom;

            // Scanlines. The period is one source pixel row, evaluated here at output
            // resolution so the gap is drawn as smoothly as the magnification allows.
            //
            // A step rather than a symmetric bump on purpose: at 2x magnification the two
            // output rows of a source row land on phases 0.25 and 0.75, and any function of
            // |phase - 0.5| gives them the same value — that is, no scanline at all where
            // scanlines are wanted most.
            float phase = fract(vUv.y * uSourceSize.y);
            float gap = smoothstep(0.45, 0.55, phase);
            color *= 1.0 - (uScanline * gap);

            // Vignette. Slightly wider than tall, because tubes were.
            float radius = length((vUv - 0.5) * vec2(1.0, 0.86));
            color *= 1.0 - (uVignette * smoothstep(0.30, 0.78, radius));

            // Unlit phosphor in a lit room is never quite black.
            color += uTint * uAmbient;

            fragColor = vec4(color, 1.0);
        }
        """;

    // ---- Public surface ---------------------------------------------------------

    /// <summary>
    /// Applies the pass, reading <paramref name="sourceTexture"/> and drawing into
    /// <paramref name="destinationFramebuffer"/> (0 for the window).
    /// </summary>
    public void Render(
        uint sourceTexture,
        int sourceWidth,
        int sourceHeight,
        uint destinationFramebuffer,
        int viewportX,
        int viewportY,
        int viewportWidth,
        int viewportHeight,
        CrtSettings crt,
        Phosphor phosphor)
    {
        ArgumentNullException.ThrowIfNull(crt);

        EnsureTargets(sourceWidth, sourceHeight);

        // Bloom chain: downsample, blur each axis, then again at half that size.
        Blit(sourceTexture, _half, horizontal: true, blur: false);
        Blur(_half, _halfScratch, horizontal: true);
        Blur(_halfScratch, _half, horizontal: false);

        Blit(_half.Texture, _quarter, horizontal: true, blur: false);
        Blur(_quarter, _quarterScratch, horizontal: true);
        Blur(_quarterScratch, _quarter, horizontal: false);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFramebuffer);
        _gl.Viewport(viewportX, viewportY, (uint)viewportWidth, (uint)viewportHeight);

        _gl.UseProgram(_compositeProgram);
        Bind(_compositeProgram, "uText", 0, sourceTexture);
        Bind(_compositeProgram, "uBloomHalf", 1, _half.Texture);
        Bind(_compositeProgram, "uBloomQuarter", 2, _quarter.Texture);

        _gl.Uniform2(_gl.GetUniformLocation(_compositeProgram, "uSourceSize"), (float)sourceWidth, (float)sourceHeight);
        _gl.Uniform1(_gl.GetUniformLocation(_compositeProgram, "uBloom"), (float)crt.Bloom);
        _gl.Uniform1(_gl.GetUniformLocation(_compositeProgram, "uScanline"), (float)crt.Scanline);
        _gl.Uniform1(_gl.GetUniformLocation(_compositeProgram, "uVignette"), (float)crt.Vignette);
        _gl.Uniform1(_gl.GetUniformLocation(_compositeProgram, "uAmbient"), (float)crt.AmbientGlow);

        var (r, g, b) = PhosphorMap.Tint(phosphor);
        _gl.Uniform3(_gl.GetUniformLocation(_compositeProgram, "uTint"), (float)r, (float)g, (float)b);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
    }

    // ---- Internals ---------------------------------------------------------------

    private void Blit(uint source, Target destination, bool horizontal, bool blur)
    {
        _ = blur;
        Draw(source, destination, horizontal, radiusTexels: 0f);
    }

    private void Blur(Target source, Target destination, bool horizontal) =>
        Draw(source.Texture, destination, horizontal, radiusTexels: 1f);

    private void Draw(uint sourceTexture, Target destination, bool horizontal, float radiusTexels)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, destination.Framebuffer);
        _gl.Viewport(0, 0, (uint)destination.Width, (uint)destination.Height);

        _gl.UseProgram(_blurProgram);
        Bind(_blurProgram, "uSource", 0, sourceTexture);

        var stepX = horizontal ? radiusTexels / destination.Width : 0f;
        var stepY = horizontal ? 0f : radiusTexels / destination.Height;
        _gl.Uniform2(_gl.GetUniformLocation(_blurProgram, "uStep"), stepX, stepY);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
    }

    private void Bind(uint program, string name, int unit, uint texture)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.Uniform1(_gl.GetUniformLocation(program, name), unit);
    }

    private void EnsureTargets(int width, int height)
    {
        if (width == _sourceWidth && height == _sourceHeight && _half.Framebuffer != 0)
        {
            return;
        }

        ReleaseTargets();

        _sourceWidth = width;
        _sourceHeight = height;

        var halfWidth = Math.Max(1, width / 2);
        var halfHeight = Math.Max(1, height / 2);
        var quarterWidth = Math.Max(1, width / 4);
        var quarterHeight = Math.Max(1, height / 4);

        _half = CreateTarget(halfWidth, halfHeight);
        _halfScratch = CreateTarget(halfWidth, halfHeight);
        _quarter = CreateTarget(quarterWidth, quarterHeight);
        _quarterScratch = CreateTarget(quarterWidth, quarterHeight);
    }

    private unsafe Target CreateTarget(int width, int height)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(
            TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);

        // Linear here, unlike the text target: these are deliberately blurry, and free
        // bilinear filtering on the downsample is exactly what is wanted.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        var framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, texture, 0);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"CRT intermediate target incomplete: {status}");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return new Target(framebuffer, texture, width, height);
    }

    private void ReleaseTargets()
    {
        foreach (var target in new[] { _half, _halfScratch, _quarter, _quarterScratch })
        {
            if (target.Framebuffer != 0)
            {
                _gl.DeleteFramebuffer(target.Framebuffer);
                _gl.DeleteTexture(target.Texture);
            }
        }

        _half = default;
        _halfScratch = default;
        _quarter = default;
        _quarterScratch = default;
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
            throw new InvalidOperationException($"CRT shader link failed: {_gl.GetProgramInfoLog(program)}");
        }

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
            throw new InvalidOperationException($"CRT {type} failed to compile: {_gl.GetShaderInfoLog(shader)}");
        }

        return shader;
    }

    public void Dispose()
    {
        ReleaseTargets();
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_blurProgram);
        _gl.DeleteProgram(_compositeProgram);
        _vao = 0;
        _blurProgram = 0;
        _compositeProgram = 0;
    }
}
