using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Baudacious.Core.Baud;
using Baudacious.Core.Commands;
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
internal sealed class TerminalWindow : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Every hosted shell, oldest first. Never empty once the window has loaded.</summary>
    private readonly List<TerminalSession> _sessions = [];
    private readonly Options _options;

    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private TextRenderer _renderer = null!;
    private GlyphAtlas _atlas = null!;

    private int _active;
    private int _nextNumber = 1;

    private DisplaySettings _settings;
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

    private bool _dragging;
    private bool _mouseButtonDown;
    private (int Row, int Column) _lastReported = (-1, -1);
    private int _clickCount;
    private long _lastClickAt;
    private GridPoint _lastClickPoint;
    private bool _helpVisible;
    private MenuKind _menu;
    private int _menuIndex;

    /// <summary>Kept for the life of the window so its history outlives one visit.</summary>
    private readonly CommandLine _commandLine = new();
    private CommandVocabulary _vocabulary = CommandVocabulary.Empty;
    private bool _promptOpen;
    private IReadOnlyList<string>? _promptMessage;
    private bool _repeatToPrompt;

    private double _idleSeconds;

    internal sealed record Options
    {
        public LineRate Rate { get; init; } = LineRate.Of(1200);

        public string Profile { get; init; } = "pwsh";

        public bool FastEcho { get; init; } = true;

        public Phosphor Phosphor { get; init; } = Phosphor.Green;

        public int FontScale { get; init; } = 2;

        public CrtSettings Crt { get; init; } = new();
    }

    /// <summary>The session the window is showing and the keyboard is talking to.</summary>
    private TerminalSession Current => _sessions[_active];

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

        // The prompt cannot invent these: palettes belong to the renderer and profiles to
        // the application, so it is told about both rather than keeping a second copy.
        _vocabulary = new CommandVocabulary
        {
            Phosphors = [.. Enum.GetNames<Phosphor>().Select(n => n.ToLowerInvariant())],
            Profiles = ShellProfiles.Names,
        };

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
        OpenSession(_options.Profile, _options.Rate, columns, rows);
    }

    // ---- Sessions ------------------------------------------------------------------

    /// <summary>
    /// Starts a shell and shows it. A profile that names something not installed leaves a
    /// grid behind with the reason on it, rather than a window with nothing in it at all.
    /// </summary>
    private void OpenSession(string profile, LineRate rate, int columns, int rows)
    {
        TerminalSession session;
        try
        {
            session = TerminalSession.Start(
                _nextNumber, profile, _options.FastEcho, rate, columns, rows, Invalidate);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            if (_sessions.Count > 0)
            {
                // There is somewhere to say this. Leave the user where they were.
                Notify(ex.Message);
                return;
            }

            session = TerminalSession.Detached(_nextNumber, profile, rate, columns, rows, Invalidate);
            Notify(ex.Message);
        }

        _nextNumber++;
        _sessions.Add(session);
        _active = _sessions.Count - 1;
        _repeatKey = Key.Unknown;
        _needsPresent = true;
    }

    /// <summary>Shows the next session, wrapping round.</summary>
    private void NextSession()
    {
        if (_sessions.Count < 2)
        {
            Notify("only one session - alt+N starts another");
            return;
        }

        _active = (_active + 1) % _sessions.Count;
        _repeatKey = Key.Unknown;
        Notify($"session {Current.Number} - {Current.Governor.Rate}");
        _needsPresent = true;
    }

    /// <summary>
    /// Drops a finished session. The window itself outlives the last one, because closing
    /// out from under someone still reading a shell's final output would be worse than a
    /// window they have to close.
    /// </summary>
    private void CloseSession(int index)
    {
        var session = _sessions[index];
        _sessions.RemoveAt(index);
        session.Dispose();

        if (index < _active)
        {
            _active--;
        }

        _active = Math.Clamp(_active, 0, Math.Max(0, _sessions.Count - 1));
        _needsPresent = true;
    }

    /// <summary>Handed to each session so anything it does can wake the frame loop.</summary>
    private void Invalidate() => _needsPresent = true;

    // ---- Option menus --------------------------------------------------------------

    /// <summary>
    /// Opens a menu, or steps it on if it is already the one open.
    /// </summary>
    /// <remarks>
    /// Pressing the chord again advancing the selection is what these keys did before they
    /// had menus, so the habit still works; the list is there for everyone who never had
    /// the habit.
    /// </remarks>
    private void OpenMenu(MenuKind kind)
    {
        if (_menu == kind)
        {
            _menuIndex = (_menuIndex + 1) % OptionMenu.Count(kind);
            ApplyMenu();
            return;
        }

        // Opening one changes nothing: it shows where you already are.
        _menu = kind;
        _menuIndex = OptionMenu.IndexOf(kind, Current.Governor.Rate, _settings.Phosphor);
        _needsPresent = true;
    }

    private void CloseMenu()
    {
        _menu = MenuKind.None;
        _needsPresent = true;
    }

    /// <summary>
    /// Menu keys while one is open. Anything else closes it and is dealt with normally, so
    /// a menu left open never swallows a keystroke.
    /// </summary>
    private bool HandleMenuKey(Key key)
    {
        var count = OptionMenu.Count(_menu);

        switch (key)
        {
            case Key.Up:
                _menuIndex = (_menuIndex + count - 1) % count;
                ApplyMenu();
                return true;

            case Key.Down:
                _menuIndex = (_menuIndex + 1) % count;
                ApplyMenu();
                return true;

            case Key.Escape or Key.Enter or Key.KeypadEnter:
                CloseMenu();
                return true;

            default:
                CloseMenu();
                return false;
        }
    }

    /// <summary>
    /// Puts the highlighted option into effect at once, so the list is a preview rather
    /// than a form to fill in.
    /// </summary>
    private void ApplyMenu()
    {
        switch (_menu)
        {
            case MenuKind.Baud:
                Current.Governor.Rate =
                    OptionMenu.RateAt(_menuIndex, Current.Governor.Rate.Framing);
                break;

            case MenuKind.Framing:
                if (Current.Governor.Rate.IsUnlimited)
                {
                    Notify("framing has no effect at an unlimited rate");
                    break;
                }

                Current.Governor.Rate = LineRate.Of(
                    Current.Governor.Rate.Baud, OptionMenu.Framings[_menuIndex]);
                break;

            case MenuKind.Phosphor:
                _settings = _settings with { Phosphor = OptionMenu.Phosphors[_menuIndex] };
                break;
        }

        _needsPresent = true;
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

        // Every session, not just the visible one: a wire does not stop carrying because
        // nobody is watching, and a paused background session would dump its whole backlog
        // the instant it was switched to.
        var released = 0;
        foreach (var session in _sessions)
        {
            released += session.Tick();
        }

        if (released > 0)
        {
            _needsPresent = true;
            _idleSeconds = 0;
        }
        else
        {
            _idleSeconds += delta;
        }

        ReapFinishedSessions();

        // A blinking cursor is a reason to repaint even when nothing arrived.
        if (_settings.CursorBlink && Current.Screen.CursorVisible)
        {
            _needsPresent = true;
        }

        if (!_needsPresent && _idleSeconds < 1.0)
        {
            _needsPresent = true;
        }

        if (_needsPresent)
        {
            var (status, caret) = StatusView();
            _renderer.RenderToTarget(
                Current.Screen, _settings, _clock.Elapsed, status, Current.Selection, Overlay(), caret);
            _renderer.Present(_window.FramebufferSize.X, _window.FramebufferSize.Y, _settings);
            _window.Title = BuildTitle();
            _needsPresent = false;
        }

        // Idle: stop spinning the GPU when nothing is happening. specs/display-rendering
        // asks for at most 10 Hz once the screen has been static for a second.
        if (_idleSeconds > 1.0 && _sessions.All(s => s.Governor.PendingBytes == 0))
        {
            Thread.Sleep(90);
        }
    }

    /// <summary>
    /// Notices shells that have exited and whose backlog has finished arriving.
    /// </summary>
    /// <remarks>
    /// The backlog matters: at 300 baud a shell can be gone for a minute while its parting
    /// words are still on the wire, and closing then would swallow them.
    /// </remarks>
    private void ReapFinishedSessions()
    {
        for (var i = _sessions.Count - 1; i >= 0; i--)
        {
            var session = _sessions[i];
            if (session.IsFinished
                || session.PtyExitCode is not { } code
                || session.Governor.PendingBytes != 0)
            {
                continue;
            }

            session.ExitCode = code;

            if (_sessions.Count == 1)
            {
                Notify($"shell exited ({code}) - close the window");
                continue;
            }

            CloseSession(i);
            Notify($"session {session.Number} exited ({code})");
        }
    }

    /// <summary>
    /// The bottom bar: line rate, what is still on the wire, and the local key bindings.
    /// specs/display-rendering wants the pending backlog visible; having the bindings
    /// there too means nobody has to remember them.
    /// </summary>
    /// <summary>
    /// What the bottom bar shows and where its caret is, if it has one.
    /// </summary>
    /// <remarks>
    /// The local prompt takes the bar over while it is open. It is the one thing allowed
    /// to put a cursor there, which is also what distinguishes it at a glance from the
    /// shell's own input line a row above.
    /// </remarks>
    private (string Status, int Caret) StatusView() =>
        _promptOpen ? PromptStatus() : (StatusLine(), -1);

    /// <summary>
    /// The panel over the grid. Only one thing can have it: what the prompt has to say
    /// first, then an open menu, then the help.
    /// </summary>
    private IReadOnlyList<string>? Overlay() =>
        _promptMessage
        ?? (_menu != MenuKind.None
            ? OptionMenu.Render(_menu, _menuIndex, Current.Governor.Rate, _settings.Phosphor)
            : null)
        ?? (_helpVisible ? Help.Lines : null);

    /// <summary>
    /// Which session is in front, or nothing at all when there is only the one. A count
    /// that never changes is noise on a bar that has 80 columns to spend.
    /// </summary>
    private string SessionMark =>
        _sessions.Count < 2
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"  #{_active + 1}/{_sessions.Count}");

    private string StatusLine()
    {
        var pending = Current.Governor.PendingBytes;
        var backlog = pending == 0
            ? "idle"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{pending} pending, {Current.Governor.EstimatedDrain.TotalSeconds:F1}s");

        // specs/terminal-emulation: while scrolled back, say so, and say that the live view
        // has moved on — otherwise a static screen looks like a hung shell.
        if (Current.Screen.IsScrolledBack)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $" {Current.Governor.Rate}{SessionMark}  [{backlog}]  -- HISTORY, {Current.Screen.ViewOffset} lines back; shift+end for live --");
        }

        if (Environment.TickCount64 < _noticeUntil)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $" {Current.Governor.Rate}{SessionMark}  [{backlog}]  {_notice}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            // Kept under 80 columns: the bar is truncated, not wrapped, so anything longer
            // is simply lost on a default-width window.
            $" {Current.Governor.Rate}{SessionMark}  [{backlog}]  alt: B=baud F=flush P=phosphor N=new  ^]=cmd");
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

        return column < 0 || row < 0 || column > Current.Screen.Columns || row >= Current.Screen.RowCount
            ? null
            : (row, Math.Min(column, Current.Screen.Columns));
    }

    /// <summary>
    /// Whether the mouse currently belongs to the hosted program rather than to selection.
    /// </summary>
    /// <remarks>
    /// specs/input-and-local-commands: holding shift takes it back, so text in a
    /// mouse-aware program can still be selected and copied.
    /// </remarks>
    private bool MouseGoesToShell =>
        Current.Emulator.MouseReporting
        && !Current.Screen.IsScrolledBack
        && !_input.Keyboards.Any(k => k.IsKeyPressed(Key.ShiftLeft) || k.IsKeyPressed(Key.ShiftRight));

    private void SendMouse(TerminalButton button, bool pressed, bool motion, int row, int column)
    {
        var keyboard = _input.Keyboards.FirstOrDefault();

        var report = MouseReport.Encode(
            Current.Emulator.MouseTracking,
            Current.Emulator.MouseEncoding,
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
            Current.Write(Encoding.UTF8.GetBytes(report));
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

        if (column < 0 || row < 0 || column > Current.Screen.Columns || row >= Current.Screen.RowCount)
        {
            return null;
        }

        return new GridPoint(Current.Screen.AbsoluteRowOf(row), Math.Min(column, Current.Screen.Columns));
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

        Current.Selection = shift && Current.Selection is not null
            ? Current.Selection with { Focus = point }          // extend what is already there
            : Selection.Expand(Current.Screen, point, mode);

        _dragging = mode == SelectionMode.Character;
        _needsPresent = true;
    }

    private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 position)
    {
        if (MouseGoesToShell)
        {
            // Drag tracking wants motion only while a button is down; motion tracking wants
            // it either way. The encoder drops what the level does not ask for.
            if ((_mouseButtonDown || Current.Emulator.MouseTracking == MouseTracking.Motion)
                && CellAt(position) is { } moved
                && (moved.Row, moved.Column) != _lastReported)
            {
                _lastReported = (moved.Row, moved.Column);
                SendMouse(TerminalButton.Left, pressed: true, motion: true, moved.Row, moved.Column);
            }

            return;
        }

        if (!_dragging || Current.Selection is null || PointAt(position) is not { } point)
        {
            return;
        }

        Current.Selection = Current.Selection with { Focus = point };
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

        if (Current.Selection is { IsEmpty: true })
        {
            Current.Selection = null;
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

        Current.Screen.ScrollView(lines);
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
        if (Current.Selection is null || Current.Selection.IsEmpty)
        {
            return;
        }

        var text = Current.Selection.GetText(Current.Screen);
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
        if (Current.Emulator.BracketedPaste)
        {
            Current.Write("\u001b[200~"u8);
            Current.Write(Encoding.UTF8.GetBytes(text));
            Current.Write("\u001b[201~"u8);
        }
        else
        {
            Current.Write(Encoding.UTF8.GetBytes(text));
        }

        // Never metered: input travels at full speed, and it is the echo coming back that
        // the governor slows down.
        Notify($"pasted {text.Length} characters");
    }

    private string BuildTitle()
    {
        var pending = Current.Governor.PendingBytes;
        var backlog = pending == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"  [{pending} pending, {Current.Governor.EstimatedDrain.TotalSeconds:F1}s]");

        var notice = Environment.TickCount64 < _noticeUntil ? "  " + _notice : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Baudacious — {Current.Governor.Rate}{SessionMark}{backlog}{notice}  {Current.Title}");
    }

    private void OnResize(Vector2D<int> size)
    {
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        _gl.Viewport(size);

        var (columns, rows) = GridFor(size);
        if (columns == Current.Screen.Columns && rows == Current.Screen.RowCount)
        {
            _needsPresent = true;
            return;
        }

        foreach (var session in _sessions)
        {
            session.Resize(columns, rows);
        }

        _needsPresent = true;
    }

    private void OnClosing()
    {
        // GL resources have to go here, not in Dispose. Closing fires with the context
        // still current; by the time Run() returns and Dispose is reached, GLFW has torn
        // the context down and every glDelete* throws NoContext.
        DisposeSessions();

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

        if (_promptOpen)
        {
            // Ctrl chords are commands at the prompt, never text — without this the ']' of
            // Ctrl+] would be typed into the line it just opened.
            var held = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
            if (!held && character >= 0x20)
            {
                _commandLine.Insert(character);
                _promptMessage = null;
                _needsPresent = true;
            }

            return;
        }

        if (character < 0x20)
        {
            return;   // control codes arrive through OnKeyDown
        }

        Current.Write(Encoding.UTF8.GetBytes(character.ToString()));
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

        var shifted = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);

        // While the prompt is up it has the whole keyboard, Alt chords included. Escape
        // gives it back, and nothing typed at it ever reaches the shell.
        if (_promptOpen)
        {
            HandlePromptKey(key, control, shifted);
            return;
        }

        // A menu claims the keys it uses and gets out of the way for everything else.
        if (_menu != MenuKind.None && !alt && HandleMenuKey(key))
        {
            return;
        }

        if (control && key == Key.RightBracket)
        {
            OpenPrompt();
            return;
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
                Current.Write([0x1B, meta]);
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

        var shift = shifted;

        // History navigation. Shift-qualified so the unmodified keys still reach the shell.
        if (shift && HandleScrollback(key))
        {
            return;
        }

        if (control && key == Key.C)
        {
            // With a selection up, Ctrl+C copies and sends nothing — interrupting the shell
            // because you wanted the text would be a poor trade.
            if (Current.Selection is { IsEmpty: false })
            {
                Copy();
                return;
            }

            var dropped = Current.Governor.Flush();
            Current.Parser.Reset();
            Current.Write([0x03]);
            Notify(dropped > 0 ? $"interrupted, {dropped} queued bytes dropped" : "interrupted");
            return;
        }

        if ((control && key == Key.V) || (shift && key == Key.Insert))
        {
            Paste();
            return;
        }

        // Typing returns to the live view; reading history should not silently swallow input.
        if (Current.Screen.IsScrolledBack && !control)
        {
            Current.Screen.ScrollToLive();
            _needsPresent = true;
        }

        var bytes = GlKeyEncoder.Encode(key, control, Current.Emulator.ApplicationCursorKeys);
        if (!bytes.IsEmpty)
        {
            Current.Write(bytes.Span);
            BeginRepeat(key, bytes);
        }
    }

    private bool HandleScrollback(Key key)
    {
        var page = Math.Max(1, Current.Screen.RowCount - 2);

        switch (key)
        {
            case Key.PageUp: Current.Screen.ScrollView(page); break;
            case Key.PageDown: Current.Screen.ScrollView(-page); break;
            case Key.Home: Current.Screen.SetViewOffset(int.MaxValue); break;
            case Key.End: Current.Screen.ScrollToLive(); break;
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
        if (key is Key.F or Key.ShiftLeft or Key.ShiftRight && Current.Governor.SpeedMultiplier != 1)
        {
            Current.Governor.SpeedMultiplier = 1;
        }
    }

    /// <summary>Windows' own defaults: about half a second before the first repeat, then ~30 a second.</summary>
    /// <summary>How long a first Alt+X stays armed. Long enough to be deliberate, short
    /// enough that it lapses before you have forgotten pressing it.</summary>
    private const long QuitConfirmMs = 3000;

    private static readonly long RepeatDelayMs = 450;
    private static readonly long RepeatIntervalMs = 33;

    private void BeginRepeat(Key key, ReadOnlyMemory<byte> bytes)
    {
        _repeatToPrompt = false;
        _repeatKey = key;
        _repeatBytes = bytes.ToArray();
        _repeatNextAt = Environment.TickCount64 + RepeatDelayMs;
        _repeatStarted = false;
    }

    private void PumpKeyRepeat()
    {
        if (_repeatKey == Key.Unknown)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now < _repeatNextAt)
        {
            return;
        }

        if (_repeatToPrompt)
        {
            // Holding backspace has to work at the local prompt too, and it edits a line
            // here rather than sending bytes anywhere.
            if (!_promptOpen || !EditPrompt(_repeatKey))
            {
                _repeatKey = Key.Unknown;
                return;
            }
        }
        else
        {
            Current.Write(_repeatBytes);
        }

        _repeatNextAt = now + RepeatIntervalMs;
        _repeatStarted = true;
        _ = _repeatStarted;
    }

    private bool HandleLocalCommand(Key key)
    {
        switch (key)
        {
            case Key.B:
                OpenMenu(MenuKind.Baud);
                return true;

            case Key.F:
                var shiftHeld = _input.Keyboards.Any(k =>
                    k.IsKeyPressed(Key.ShiftLeft) || k.IsKeyPressed(Key.ShiftRight));

                if (shiftHeld)
                {
                    // Held, not toggled: released in OnKeyUp. Skimming is something you do
                    // while watching, not a mode you enter.
                    Current.Governor.SpeedMultiplier = 10;
                    Notify("10x while held");
                    return true;
                }

                var pending = Current.Governor.PendingBytes;
                Current.Governor.FastForward();
                Notify(pending > 0 ? $"fast-forwarding {pending} bytes" : "nothing queued");
                return true;

            case Key.G:
                OpenMenu(MenuKind.Framing);
                return true;

            case Key.N:
                // The same profile as the one in front: a second window on the same kind of
                // thing is what "another one" nearly always means.
                OpenSession(Current.Profile, Current.Governor.Rate, Current.Screen.Columns, Current.Screen.RowCount);
                return true;

            case Key.S:
                NextSession();
                return true;

            case Key.Z:
                _helpVisible = !_helpVisible;
                _needsPresent = true;
                return true;

            case Key.P:
                OpenMenu(MenuKind.Phosphor);
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

    // ---- The local command prompt -------------------------------------------------

    /// <summary>
    /// What the prompt is introduced by. Deliberately not a shell-looking prompt: whatever
    /// is typed here never leaves the client.
    /// </summary>
    private const string PromptPrefix = " ] ";

    private void OpenPrompt()
    {
        _promptOpen = true;
        _promptMessage = null;
        _helpVisible = false;
        _repeatKey = Key.Unknown;
        _commandLine.Clear();
        _needsPresent = true;
    }

    private void ClosePrompt()
    {
        _promptOpen = false;
        _promptMessage = null;
        _repeatKey = Key.Unknown;
        _needsPresent = true;
    }

    /// <summary>The bar while the prompt is open, and the column its cursor sits in.</summary>
    private (string Status, int Caret) PromptStatus()
    {
        var width = Math.Max(8, Current.Screen.Columns - PromptPrefix.Length - 1);
        var text = _commandLine.Text;
        var caret = _commandLine.Caret;

        // Slide the window so the caret stays visible. Commands are short enough that this
        // rarely matters; a pasted path is exactly when it does.
        var start = Math.Max(0, caret - width + 1);
        var visible = text[start..Math.Min(text.Length, start + width)];

        return (PromptPrefix + visible, PromptPrefix.Length + (caret - start));
    }

    private void HandlePromptKey(Key key, bool control, bool shift)
    {
        if (control)
        {
            switch (key)
            {
                case Key.RightBracket or Key.G:
                    ClosePrompt();
                    return;

                case Key.C:
                    // As at any line editor: abandon the line, keep the prompt.
                    _commandLine.Clear();
                    _promptMessage = null;
                    _needsPresent = true;
                    return;

                case Key.U:
                    _commandLine.DeleteToStart();
                    _promptMessage = null;
                    _needsPresent = true;
                    return;

                case Key.W:
                    _commandLine.DeleteWordBack();
                    _promptMessage = null;
                    _needsPresent = true;
                    return;

                case Key.A:
                    _commandLine.MoveHome();
                    _needsPresent = true;
                    return;

                case Key.E:
                    _commandLine.MoveEnd();
                    _needsPresent = true;
                    return;

                case Key.V:
                    PasteIntoPrompt();
                    return;
            }
        }

        if (shift && key == Key.Insert)
        {
            PasteIntoPrompt();
            return;
        }

        switch (key)
        {
            case Key.Escape:
                ClosePrompt();
                return;

            case Key.Enter or Key.KeypadEnter:
                var line = _commandLine.Submit();
                Run(CommandParser.Parse(line, _vocabulary), line);
                return;

            case Key.Tab:
                CompletePrompt();
                return;

            case Key.Up:
                _commandLine.HistoryPrevious();
                _promptMessage = null;
                _needsPresent = true;
                return;

            case Key.Down:
                _commandLine.HistoryNext();
                _promptMessage = null;
                _needsPresent = true;
                return;

            case Key.Home:
                _commandLine.MoveHome();
                _needsPresent = true;
                return;

            case Key.End:
                _commandLine.MoveEnd();
                _needsPresent = true;
                return;
        }

        if (EditPrompt(key))
        {
            _repeatToPrompt = true;
            _repeatKey = key;
            _repeatNextAt = Environment.TickCount64 + RepeatDelayMs;
        }
    }

    /// <summary>The editing keys worth repeating while held.</summary>
    private bool EditPrompt(Key key)
    {
        switch (key)
        {
            case Key.Backspace: _commandLine.Backspace(); break;
            case Key.Delete: _commandLine.Delete(); break;
            case Key.Left: _commandLine.MoveLeft(); break;
            case Key.Right: _commandLine.MoveRight(); break;
            default: return false;
        }

        _promptMessage = null;
        _needsPresent = true;
        return true;
    }

    private void PasteIntoPrompt()
    {
        if (Clipboard.GetText() is not { Length: > 0 } text)
        {
            return;
        }

        // One line only: the prompt is one line, and a pasted block would otherwise arrive
        // as a single run-together command.
        var end = text.AsSpan().IndexOfAny('\r', '\n');
        _commandLine.Insert(end < 0 ? text : text[..end]);
        _promptMessage = null;
        _needsPresent = true;
    }

    private void CompletePrompt()
    {
        var completion = _commandLine.Complete(_vocabulary);
        _promptMessage = completion.Candidates.Count == 0 ? null : Columns(completion.Candidates);
        _needsPresent = true;
    }

    /// <summary>Lays candidates out across the panel rather than one to a line.</summary>
    private static string[] Columns(IReadOnlyList<string> candidates)
    {
        const int Width = 52;

        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (var candidate in candidates)
        {
            if (line.Length > 0 && line.Length + candidate.Length + 2 > Width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append("  ");
            }

            line.Append(candidate);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return [.. lines];
    }

    private static string[] PromptHelp() =>
        ["local commands - Ctrl+] opens, Escape closes", string.Empty, .. CommandVocabulary.Usage];

    /// <summary>
    /// Carries out a parsed command. Anything that succeeded closes the prompt and says so
    /// in the bar; anything that did not leaves the prompt open with the reason.
    /// </summary>
    /// <param name="line">
    /// What was typed. Anything that leaves the prompt open puts it back, so a typo is
    /// corrected rather than retyped.
    /// </param>
    private void Run(LocalCommand command, string line)
    {
        switch (command)
        {
            case LocalCommand.Nothing:
                ClosePrompt();
                return;

            case LocalCommand.Failed failed:
                Refuse(failed.Message, line);
                return;

            case LocalCommand.ShowHelp:
                _promptMessage = PromptHelp();
                _needsPresent = true;
                return;

            case LocalCommand.Quit:
                ClosePrompt();
                _window.Close();
                return;

            case LocalCommand.SetRate rate:
                Current.Governor.Rate = rate.Rate;
                Finish($"line rate now {Current.Governor.Rate}");
                return;

            case LocalCommand.SetFraming framing:
                if (Current.Governor.Rate.IsUnlimited)
                {
                    Refuse("framing has no effect at an unlimited rate", line);
                    return;
                }

                Current.Governor.Rate = LineRate.Of(Current.Governor.Rate.Baud, framing.Framing);
                Finish(string.Create(
                    CultureInfo.InvariantCulture,
                    $"framing {framing.Framing} - {Current.Governor.Rate.BytesPerSecond:F1} chars/sec"));
                return;

            case LocalCommand.Flush:
                var pending = Current.Governor.PendingBytes;
                Current.Governor.FastForward();
                Finish(pending > 0 ? $"fast-forwarding {pending} bytes" : "nothing queued");
                return;

            case LocalCommand.SetPhosphor phosphor:
                if (!Enum.TryParse<Phosphor>(phosphor.Name, ignoreCase: true, out var palette))
                {
                    Refuse($"no phosphor named '{phosphor.Name}'", line);
                    return;
                }

                _settings = _settings with { Phosphor = palette };
                Finish($"phosphor: {palette}");
                return;

            case LocalCommand.SetFontSize size:
                ClosePrompt();
                SetFontScale(size.Scale);
                return;

            case LocalCommand.SetCrt crt:
                var on = crt.On ?? !_settings.Crt.Enabled;
                _settings = _settings with { Crt = _settings.Crt with { Enabled = on } };
                Finish(on ? "CRT on" : "CRT off");
                return;

            case LocalCommand.NewSession fresh:
                var columns = Current.Screen.Columns;
                var rows = Current.Screen.RowCount;
                var inherited = Current.Governor.Rate;
                ClosePrompt();
                OpenSession(fresh.Profile, inherited, columns, rows);
                return;
        }
    }

    /// <summary>Done: the prompt goes away and the bar reports it.</summary>
    private void Finish(string message)
    {
        ClosePrompt();
        Notify(message);
    }

    /// <summary>Not done: the prompt stays, carrying the reason and the line to fix.</summary>
    private void Refuse(string message, string line)
    {
        _commandLine.Insert(line);
        _promptMessage = [message];
        _needsPresent = true;
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

    private void DisposeSessions()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }

    public void Dispose()
    {
        // Everything GL-shaped was already released in OnClosing, while the context was
        // still current. Only the window itself is left.
        DisposeSessions();
        _window?.Dispose();
    }
}
