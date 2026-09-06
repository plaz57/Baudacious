using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Baudacious.Core.Baud;
using Baudacious.Core.Terminal;
using TerminalButton = Baudacious.Core.Terminal.MouseButton;
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

    // Silk.NET raises KeyDown once per physical press and never for the OS repeat, so
    // printable characters repeat (they arrive as KeyChar, which does repeat) while
    // Backspace, Delete and the arrows do not. These drive our own repeat for that second
    // group only, so nothing ends up repeating twice.
    private Key _repeatKey = Key.Unknown;
    private byte[] _repeatBytes = [];
    private long _repeatNextAt;
    private bool _repeatStarted;
    /// <summary>
    /// When the quit chord is armed until. Quitting kills a live shell and everything in
    /// it, and Alt+X sits one key away from bindings pressed casually, so a single press
    /// only arms it.
    /// </summary>
    private long _quitArmedUntil;

    private Selection? _selection;
    private bool _dragging;
    private bool _mouseButtonDown;
    private (int Row, int Column) _lastReported = (-1, -1);
    private int _clickCount;
    private long _lastClickAt;
    private GridPoint _lastClickPoint;
    private bool _helpVisible;

    private double _idleSeconds;
    private int? _exitCode;

    internal sealed record Options
    {
        public LineRate Rate { get; init; } = LineRate.Of(1200);

        public string Profile { get; init; } = "pwsh";

        public bool FastEcho { get; init; } = true;

        public Phosphor Phosphor { get; init; } = Phosphor.Green;

        public int FontScale { get; init; } = 2;

        public CrtSettings Crt { get; init; } = new();
    }

    private TerminalWindow(Options options)
    {
        _options = options;
        _settings = new DisplaySettings
        {
            Phosphor = options.Phosphor,
            FontScale = options.FontScale,
            Crt = options.Crt,
        };
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
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnScroll;
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
        catch (InvalidOperationException ex)
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

        // One row is reserved for the status bar, which lives below the grid rather than
        // inside it.
        var rows = Math.Max(5, (framebuffer.Y / (_atlas.CellHeight * scale)) - 1);
        return (columns, rows);
    }

    // ---- Frame loop ------------------------------------------------------------

    private void OnRender(double delta)
    {
        PumpKeyRepeat();

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
            _renderer.RenderToTarget(
                _screen, _settings, _clock.Elapsed, StatusLine(), _selection, _helpVisible ? Help.Lines : null);
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

    /// <summary>
    /// The bottom bar: line rate, what is still on the wire, and the local key bindings.
    /// specs/display-rendering wants the pending backlog visible; having the bindings
    /// there too means nobody has to remember them.
    /// </summary>
    private string StatusLine()
    {
        var pending = _governor.PendingBytes;
        var backlog = pending == 0
            ? "idle"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{pending} pending, {_governor.EstimatedDrain.TotalSeconds:F1}s");

        // specs/terminal-emulation: while scrolled back, say so, and say that the live view
        // has moved on — otherwise a static screen looks like a hung shell.
        if (_screen.IsScrolledBack)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $" {_governor.Rate}  [{backlog}]  -- HISTORY, {_screen.ViewOffset} lines back; shift+end for live --");
        }

        if (Environment.TickCount64 < _noticeUntil)
        {
            return string.Create(CultureInfo.InvariantCulture, $" {_governor.Rate}  [{backlog}]  {_notice}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            // Kept under 80 columns: the bar is truncated, not wrapped, so anything longer
            // is simply lost on a default-width window.
            $" {_governor.Rate}  [{backlog}]  alt: B=baud F=flush P=phosphor C=crt X=quit");
    }

    // ---- Mouse, selection and scrollback ----------------------------------------

    /// <summary>Window pixel to the grid cell under it, as a screen row and column.</summary>
    private (int Row, int Column)? CellAt(System.Numerics.Vector2 position)
    {
        var scale = Math.Max(1, Math.Min(
            _window.FramebufferSize.X / Math.Max(1, _renderer.TargetWidth),
            _window.FramebufferSize.Y / Math.Max(1, _renderer.TargetHeight)));

        var originX = (_window.FramebufferSize.X - (_renderer.TargetWidth * scale)) / 2;
        var originY = (_window.FramebufferSize.Y - (_renderer.TargetHeight * scale)) / 2;

        var column = (int)((position.X - originX) / (_atlas.CellWidth * scale));
        var row = (int)((position.Y - originY) / (_atlas.CellHeight * scale));

        return column < 0 || row < 0 || column > _screen.Columns || row >= _screen.RowCount
            ? null
            : (row, Math.Min(column, _screen.Columns));
    }

    /// <summary>
    /// Whether the mouse currently belongs to the hosted program rather than to selection.
    /// </summary>
    /// <remarks>
    /// specs/input-and-local-commands: holding shift takes it back, so text in a
    /// mouse-aware program can still be selected and copied.
    /// </remarks>
    private bool MouseGoesToShell =>
        _emulator.MouseReporting
        && !_screen.IsScrolledBack
        && !_input.Keyboards.Any(k => k.IsKeyPressed(Key.ShiftLeft) || k.IsKeyPressed(Key.ShiftRight));

    private void SendMouse(TerminalButton button, bool pressed, bool motion, int row, int column)
    {
        var keyboard = _input.Keyboards.FirstOrDefault();

        var report = MouseReport.Encode(
            _emulator.MouseTracking,
            _emulator.MouseEncoding,
            button,
            pressed,
            motion,
            column,
            row,
            shift: false,
            alt: keyboard?.IsKeyPressed(Key.AltLeft) == true || keyboard?.IsKeyPressed(Key.AltRight) == true,
            control: keyboard?.IsKeyPressed(Key.ControlLeft) == true || keyboard?.IsKeyPressed(Key.ControlRight) == true);

        if (report is not null)
        {
            _session?.Write(Encoding.UTF8.GetBytes(report));
        }
    }

    /// <summary>Window pixel to the grid cell under it, in absolute line numbering.</summary>
    private GridPoint? PointAt(System.Numerics.Vector2 position)
    {
        var scale = Math.Max(1, Math.Min(
            _window.FramebufferSize.X / Math.Max(1, _renderer.TargetWidth),
            _window.FramebufferSize.Y / Math.Max(1, _renderer.TargetHeight)));

        // The image is centred, so the margins have to come off before dividing.
        var originX = (_window.FramebufferSize.X - (_renderer.TargetWidth * scale)) / 2;
        var originY = (_window.FramebufferSize.Y - (_renderer.TargetHeight * scale)) / 2;

        var column = (int)((position.X - originX) / (_atlas.CellWidth * scale));
        var row = (int)((position.Y - originY) / (_atlas.CellHeight * scale));

        if (column < 0 || row < 0 || column > _screen.Columns || row >= _screen.RowCount)
        {
            return null;
        }

        return new GridPoint(_screen.AbsoluteRowOf(row), Math.Min(column, _screen.Columns));
    }

    private void OnMouseDown(IMouse mouse, Silk.NET.Input.MouseButton button)
    {
        if (MouseGoesToShell && CellAt(mouse.Position) is { } cell)
        {
            SendMouse(ToTerminalButton(button), pressed: true, motion: false, cell.Row, cell.Column);
            _mouseButtonDown = true;
            return;
        }

        if (button != Silk.NET.Input.MouseButton.Left || PointAt(mouse.Position) is not { } point)
        {
            return;
        }

        var now = Environment.TickCount64;
        var quick = now - _lastClickAt < 400 && point.Line == _lastClickPoint.Line
                    && Math.Abs(point.Column - _lastClickPoint.Column) <= 1;

        _clickCount = quick ? Math.Min(_clickCount + 1, 3) : 1;
        _lastClickAt = now;
        _lastClickPoint = point;

        var mode = _clickCount switch
        {
            2 => SelectionMode.Word,
            3 => SelectionMode.Line,
            _ => SelectionMode.Character,
        };

        var shift = _input.Keyboards.Any(k => k.IsKeyPressed(Key.ShiftLeft) || k.IsKeyPressed(Key.ShiftRight));

        _selection = shift && _selection is not null
            ? _selection with { Focus = point }          // extend what is already there
            : Selection.Expand(_screen, point, mode);

        _dragging = mode == SelectionMode.Character;
        _needsPresent = true;
    }

    private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 position)
    {
        if (MouseGoesToShell)
        {
            // Drag tracking wants motion only while a button is down; motion tracking wants
            // it either way. The encoder drops what the level does not ask for.
            if ((_mouseButtonDown || _emulator.MouseTracking == MouseTracking.Motion)
                && CellAt(position) is { } moved
                && (moved.Row, moved.Column) != _lastReported)
            {
                _lastReported = (moved.Row, moved.Column);
                SendMouse(TerminalButton.Left, pressed: true, motion: true, moved.Row, moved.Column);
            }

            return;
        }

        if (!_dragging || _selection is null || PointAt(position) is not { } point)
        {
            return;
        }

        _selection = _selection with { Focus = point };
        _needsPresent = true;
    }

    private void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button)
    {
        _mouseButtonDown = false;

        if (MouseGoesToShell && CellAt(mouse.Position) is { } cell)
        {
            SendMouse(ToTerminalButton(button), pressed: false, motion: false, cell.Row, cell.Column);
            return;
        }

        if (button != Silk.NET.Input.MouseButton.Left)
        {
            return;
        }

        _dragging = false;

        if (_selection is { IsEmpty: true })
        {
            _selection = null;
            _needsPresent = true;
        }
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        var lines = (int)Math.Round(wheel.Y) * 3;
        if (lines == 0)
        {
            return;
        }

        // A program that tracks the mouse gets the wheel too — that is how a pager or an
        // editor scrolls its own buffer rather than ours.
        if (MouseGoesToShell && CellAt(mouse.Position) is { } cell)
        {
            var button = lines > 0 ? TerminalButton.WheelUp : TerminalButton.WheelDown;
            for (var i = 0; i < Math.Min(Math.Abs(lines), 9); i++)
            {
                SendMouse(button, pressed: true, motion: false, cell.Row, cell.Column);
            }

            return;
        }

        _screen.ScrollView(lines);
        _needsPresent = true;
    }

    private static TerminalButton ToTerminalButton(Silk.NET.Input.MouseButton button) => button switch
    {
        Silk.NET.Input.MouseButton.Middle => TerminalButton.Middle,
        Silk.NET.Input.MouseButton.Right => TerminalButton.Right,
        _ => TerminalButton.Left,
    };

    private void Copy()
    {
        if (_selection is null || _selection.IsEmpty)
        {
            return;
        }

        var text = _selection.GetText(_screen);
        Notify(Clipboard.SetText(text)
            ? $"copied {text.Length} characters"
            : "could not open the clipboard");
    }

    private void Paste()
    {
        if (Clipboard.GetText() is not { Length: > 0 } text)
        {
            return;
        }

        // Normalise to CR, which is what a terminal sends for Enter.
        text = text.ReplaceLineEndings("\r");

        // Bracketed paste tells the shell this arrived as a paste rather than as typing, so
        // a multi-line paste is not run line by line as it lands.
        if (_emulator.BracketedPaste)
        {
            _session?.Write("\u001b[200~"u8);
            _session?.Write(Encoding.UTF8.GetBytes(text));
            _session?.Write("\u001b[201~"u8);
        }
        else
        {
            _session?.Write(Encoding.UTF8.GetBytes(text));
        }

        // Never metered: input travels at full speed, and it is the echo coming back that
        // the governor slows down.
        Notify($"pasted {text.Length} characters");
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
        // GL resources have to go here, not in Dispose. Closing fires with the context
        // still current; by the time Run() returns and Dispose is reached, GLFW has torn
        // the context down and every glDelete* throws NoContext.
        _session?.Dispose();
        _session = null;

        _renderer?.Dispose();
        _renderer = null!;

        _input?.Dispose();
        _input = null!;
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

        if (_quitArmedUntil != 0 && !(alt && key == Key.X))
        {
            // Any other key means they did not mean to quit.
            _quitArmedUntil = 0;
        }

        if (alt)
        {
            if (HandleLocalCommand(key))
            {
                return;
            }

            // Unbound Alt chords are the shell's. Sending ESC + the key is what an
            // xterm-compatible terminal does with Meta, and it is why the local layer lives
            // on Alt at all: it can hand back anything it does not claim.
            if (GlKeyEncoder.MetaByte(key) is { } meta)
            {
                _session?.Write([0x1B, meta]);
                return;
            }

            return;
        }

        if (_helpVisible && key == Key.Escape)
        {
            _helpVisible = false;
            _needsPresent = true;
            return;
        }

        var shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);

        // History navigation. Shift-qualified so the unmodified keys still reach the shell.
        if (shift && HandleScrollback(key))
        {
            return;
        }

        if (control && key == Key.C)
        {
            // With a selection up, Ctrl+C copies and sends nothing — interrupting the shell
            // because you wanted the text would be a poor trade.
            if (_selection is { IsEmpty: false })
            {
                Copy();
                return;
            }

            var dropped = _governor.Flush();
            _parser.Reset();
            _session?.Write([0x03]);
            Notify(dropped > 0 ? $"interrupted, {dropped} queued bytes dropped" : "interrupted");
            return;
        }

        if ((control && key == Key.V) || (shift && key == Key.Insert))
        {
            Paste();
            return;
        }

        // Typing returns to the live view; reading history should not silently swallow input.
        if (_screen.IsScrolledBack && !control)
        {
            _screen.ScrollToLive();
            _needsPresent = true;
        }

        var bytes = GlKeyEncoder.Encode(key, control, _emulator.ApplicationCursorKeys);
        if (!bytes.IsEmpty)
        {
            _session?.Write(bytes.Span);
            BeginRepeat(key, bytes);
        }
    }

    private bool HandleScrollback(Key key)
    {
        var page = Math.Max(1, _screen.RowCount - 2);

        switch (key)
        {
            case Key.PageUp: _screen.ScrollView(page); break;
            case Key.PageDown: _screen.ScrollView(-page); break;
            case Key.Home: _screen.SetViewOffset(int.MaxValue); break;
            case Key.End: _screen.ScrollToLive(); break;
            default: return false;
        }

        _needsPresent = true;
        return true;
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int _)
    {
        if (key == _repeatKey)
        {
            _repeatKey = Key.Unknown;
        }

        // Letting go of either half of Alt+Shift+F ends the skim.
        if (key is Key.F or Key.ShiftLeft or Key.ShiftRight && _governor.SpeedMultiplier != 1)
        {
            _governor.SpeedMultiplier = 1;
        }
    }

    /// <summary>
    /// Steps through the framings. 8N1 and 7E1 are both ten bits per character; 8N2 and 8E1
    /// are eleven, so the same baud runs measurably slower.
    /// </summary>
    private void CycleFraming()
    {
        if (_governor.Rate.IsUnlimited)
        {
            Notify("framing has no effect at an unlimited rate");
            return;
        }

        Framing[] order = [Framing.N81, Framing.E71, Framing.N82, Framing.E81];
        var next = order[(Array.IndexOf(order, _governor.Rate.Framing) + 1) % order.Length];

        _governor.Rate = LineRate.Of(_governor.Rate.Baud, next);
        Notify(string.Create(
            CultureInfo.InvariantCulture,
            $"framing {next} — {_governor.Rate.BytesPerSecond:F1} chars/sec"));
    }

    /// <summary>Windows' own defaults: about half a second before the first repeat, then ~30 a second.</summary>
    /// <summary>How long a first Alt+X stays armed. Long enough to be deliberate, short
    /// enough that it lapses before you have forgotten pressing it.</summary>
    private const long QuitConfirmMs = 3000;

    private static readonly long RepeatDelayMs = 450;
    private static readonly long RepeatIntervalMs = 33;

    private void BeginRepeat(Key key, ReadOnlyMemory<byte> bytes)
    {
        _repeatKey = key;
        _repeatBytes = bytes.ToArray();
        _repeatNextAt = Environment.TickCount64 + RepeatDelayMs;
        _repeatStarted = false;
    }

    private void PumpKeyRepeat()
    {
        if (_repeatKey == Key.Unknown || _session is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now < _repeatNextAt)
        {
            return;
        }

        _session.Write(_repeatBytes);
        _repeatNextAt = now + RepeatIntervalMs;
        _repeatStarted = true;
        _ = _repeatStarted;
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
                var shiftHeld = _input.Keyboards.Any(k =>
                    k.IsKeyPressed(Key.ShiftLeft) || k.IsKeyPressed(Key.ShiftRight));

                if (shiftHeld)
                {
                    // Held, not toggled: released in OnKeyUp. Skimming is something you do
                    // while watching, not a mode you enter.
                    _governor.SpeedMultiplier = 10;
                    Notify("10x while held");
                    return true;
                }

                var pending = _governor.PendingBytes;
                _governor.FastForward();
                Notify(pending > 0 ? $"fast-forwarding {pending} bytes" : "nothing queued");
                return true;

            case Key.G:
                CycleFraming();
                return true;

            case Key.Z:
                _helpVisible = !_helpVisible;
                _needsPresent = true;
                return true;

            case Key.P:
                _settings = _settings with { Phosphor = (Phosphor)(((int)_settings.Phosphor + 1) % 4) };
                Notify($"phosphor: {_settings.Phosphor}");
                return true;

            case Key.C:
                var on = !_settings.Crt.Enabled;
                _settings = _settings with { Crt = _settings.Crt with { Enabled = on } };
                Notify(on ? "CRT on" : "CRT off");
                return true;

            case Key.Equal or Key.KeypadAdd:
                SetFontScale(_settings.FontScale + 1);
                return true;

            case Key.Minus or Key.KeypadSubtract:
                SetFontScale(_settings.FontScale - 1);
                return true;

            case Key.X:
                if (Environment.TickCount64 < _quitArmedUntil)
                {
                    _window.Close();
                }
                else
                {
                    _quitArmedUntil = Environment.TickCount64 + QuitConfirmMs;
                    Notify("press Alt+X again to quit");
                }

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
        // Everything GL-shaped was already released in OnClosing, while the context was
        // still current. Only the window itself is left.
        _session?.Dispose();
        _session = null;
        _window?.Dispose();
    }
}
