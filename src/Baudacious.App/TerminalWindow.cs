using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Baudacious.Core.Baud;
using Baudacious.Core.Terminal;
using Baudacious.Core.Timing;
using Baudacious.Pty;
using Baudacious.Render;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Baudacious.App;

/// <summary>
/// Baudacious in its own window: a shell hosted through ConPTY, metered at a line rate,
/// parsed, and drawn on the GPU.
/// </summary>
/// <remarks>
/// The pipeline is unchanged from the console harness — ConPTY, ring, governor, parser,
/// grid — with the console renderer swapped for <see cref="TextRenderer"/>. That swap was
/// the whole point of keeping the grid a plain data structure with no idea how it is drawn.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class TerminalWindow : IPtyOutputSink, ITerminalHost, IDisposable
{
    private readonly ByteRing _ring = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Options _options;

    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private TextRenderer _renderer = null!;
    private GlyphAtlas _atlas = null!;

    private Screen _screen = null!;
    private TerminalEmulator _emulator = null!;
    private VtParser _parser = null!;
    private BaudGovernor _governor = null!;
    private PtySession? _session;

    private DisplaySettings _settings;
    private string _title = string.Empty;
    private string _notice = string.Empty;
    private long _noticeUntil;
    private bool _needsPresent = true;
    private double _idleSeconds;
    private int? _exitCode;

    internal sealed record Options
    {
        public LineRate Rate { get; init; } = LineRate.Of(1200);

        public string Profile { get; init; } = "pwsh";

        public bool FastEcho { get; init; } = true;

        public Phosphor Phosphor { get; init; } = Phosphor.Green;

        public int FontScale { get; init; } = 2;
    }

    private TerminalWindow(Options options)
    {
        _options = options;
        _settings = new DisplaySettings { Phosphor = options.Phosphor, FontScale = options.FontScale };
    }

    public static int Run(Options options)
    {
        using var terminal = new TerminalWindow(options);
        return terminal.Execute();
    }

    private int Execute()
    {
        var windowOptions = WindowOptions.Default with
        {
            Size = new Vector2D<int>(80 * 8 * _options.FontScale, (25 * 16 * _options.FontScale) + 20),
            Title = "Baudacious",
            VSync = true,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.Default,
                new APIVersion(3, 3)),
        };

        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += OnResize;
        _window.Closing += OnClosing;

        _window.Run();
        return 0;
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _atlas = GlyphAtlas.Build();
        _renderer = new TextRenderer(_gl, _atlas);

        _input = _window.CreateInput();
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyChar += OnKeyChar;
        }

        var (columns, rows) = GridFor(_window.FramebufferSize);
        _screen = new Screen(columns, rows);
        _emulator = new TerminalEmulator(_screen, this);
        _parser = new VtParser(_emulator);
        _governor = new BaudGovernor(_ring, SystemTimeSource.Instance, _options.Rate);

        StartShell(columns, rows);
    }

    private void StartShell(int columns, int rows)
    {
        PtyStartInfo start;
        string[] setup;
        try
        {
            (start, setup) = ShellProfiles.Build(_options.Profile, _options.FastEcho);
        }
        catch (FileNotFoundException ex)
        {
            Notify(ex.Message);
            return;
        }

        _session = PtySession.Start(start with { Columns = (short)columns, Rows = (short)rows }, this);

        if (setup.Length == 0)
        {
            return;
        }

        // Off the render thread, because the shell has to come up before it will read: a
        // startup command delivered too early is simply lost.
        var session = _session;
        _ = Task.Run(async () =>
        {
            var deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline && _ring.IsEmpty)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            await Task.Delay(250).ConfigureAwait(false);

            foreach (var command in setup)
            {
                session.Write(Encoding.UTF8.GetBytes(command + "\r"));
                await Task.Delay(120).ConfigureAwait(false);
            }
        });
    }

    private (int Columns, int Rows) GridFor(Vector2D<int> framebuffer)
    {
        var scale = Math.Max(1, _settings.FontScale);
        var columns = Math.Max(20, framebuffer.X / (_atlas.CellWidth * scale));
        var rows = Math.Max(5, (framebuffer.Y - (16 * scale)) / (_atlas.CellHeight * scale));
        return (columns, rows);
    }

    // ---- Frame loop ------------------------------------------------------------

    private void OnRender(double delta)
    {
        var released = _governor.Tick(_parser);
        if (released > 0)
        {
            _needsPresent = true;
            _idleSeconds = 0;
        }
        else
        {
            _idleSeconds += delta;
        }

        if (_exitCode is null && _session?.TryGetExitCode() is { } code && _governor.PendingBytes == 0)
        {
            _exitCode = code;
            Notify($"shell exited ({code}) — close the window");
        }

        // A blinking cursor is a reason to repaint even when nothing arrived.
        if (_settings.CursorBlink && _screen.CursorVisible)
        {
            _needsPresent = true;
        }

        if (!_needsPresent && _idleSeconds < 1.0)
        {
            _needsPresent = true;
        }

        if (_needsPresent)
        {
            _renderer.RenderToTarget(_screen, _settings, _clock.Elapsed);
            _renderer.Present(_window.FramebufferSize.X, _window.FramebufferSize.Y, _settings);
            _window.Title = BuildTitle();
            _needsPresent = false;
        }

        // Idle: stop spinning the GPU when nothing is happening. specs/display-rendering
        // asks for at most 10 Hz once the screen has been static for a second.
        if (_idleSeconds > 1.0 && _governor.PendingBytes == 0)
        {
            Thread.Sleep(90);
        }
    }

    private string BuildTitle()
    {
        var pending = _governor.PendingBytes;
        var backlog = pending == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"  [{pending} pending, {_governor.EstimatedDrain.TotalSeconds:F1}s]");

        var notice = Environment.TickCount64 < _noticeUntil ? "  " + _notice : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Baudacious — {_governor.Rate}{backlog}{notice}  {_title}");
    }

    private void OnResize(Vector2D<int> size)
    {
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        _gl.Viewport(size);

        var (columns, rows) = GridFor(size);
        if (columns == _screen.Columns && rows == _screen.RowCount)
        {
            _needsPresent = true;
            return;
        }

        // Reflow first, then tell ConPTY: the queued bytes are neither discarded nor
        // reordered by either step.
        _screen.Resize(columns, rows);
        _session?.Resize((short)columns, (short)rows);
        _needsPresent = true;
    }

    private void OnClosing()
    {
        _session?.Dispose();
        _session = null;
    }

    // ---- Input ------------------------------------------------------------------

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        if (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight))
        {
            return;   // the Alt layer is ours; see OnKeyDown
        }

        if (character < 0x20)
        {
            return;   // control codes arrive through OnKeyDown
        }

        _session?.Write(Encoding.UTF8.GetBytes(character.ToString()));
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int _)
    {
        var alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
        var control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

        if (alt && HandleLocalCommand(key))
        {
            return;
        }

        if (control && key == Key.C)
        {
            var dropped = _governor.Flush();
            _parser.Reset();
            _session?.Write([0x03]);
            Notify(dropped > 0 ? $"interrupted, {dropped} queued bytes dropped" : "interrupted");
            return;
        }

        var bytes = GlKeyEncoder.Encode(key, control, _emulator.ApplicationCursorKeys);
        if (!bytes.IsEmpty)
        {
            _session?.Write(bytes.Span);
        }
    }

    private bool HandleLocalCommand(Key key)
    {
        switch (key)
        {
            case Key.B:
                var rates = LineRate.StandardBauds;
                var current = Array.IndexOf(rates, _governor.Rate.Baud);
                _governor.Rate = LineRate.Of(rates[(current + 1) % rates.Length]);
                Notify($"line rate now {_governor.Rate}");
                return true;

            case Key.F:
                var pending = _governor.PendingBytes;
                _governor.FastForward();
                Notify(pending > 0 ? $"fast-forwarding {pending} bytes" : "nothing queued");
                return true;

            case Key.P:
                _settings = _settings with { Phosphor = (Phosphor)(((int)_settings.Phosphor + 1) % 4) };
                Notify($"phosphor: {_settings.Phosphor}");
                return true;

            case Key.Equal or Key.KeypadAdd:
                SetFontScale(_settings.FontScale + 1);
                return true;

            case Key.Minus or Key.KeypadSubtract:
                SetFontScale(_settings.FontScale - 1);
                return true;

            case Key.X:
                _window.Close();
                return true;

            default:
                return false;
        }
    }

    private void SetFontScale(int scale)
    {
        scale = Math.Clamp(scale, 1, 8);
        if (scale == _settings.FontScale)
        {
            return;
        }

        _settings = _settings with { FontScale = scale };
        Notify($"font scale {scale}x");
        OnResize(_window.FramebufferSize);
    }

    private void Notify(string message)
    {
        _notice = message;
        _noticeUntil = Environment.TickCount64 + 2500;
        _needsPresent = true;
    }

    // ---- Plumbing ----------------------------------------------------------------

    public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp) => _ring.Write(bytes);

    public void OnClosed() => _needsPresent = true;

    public void Reply(ReadOnlySpan<byte> bytes) => _session?.Write(bytes);

    public void SetTitle(string title)
    {
        _title = title;
        _needsPresent = true;
    }

    public void Bell() => _needsPresent = true;

    public void Dispose()
    {
        _session?.Dispose();
        _renderer?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}
