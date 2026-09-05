using Baudacious.Core.Baud;

namespace Baudacious.Core.Terminal;

/// <summary>
/// The DEC/Williams ANSI escape-sequence state machine, driven one byte at a time.
/// </summary>
/// <remarks>
/// <para>
/// All state lives in fields, which is the property that matters most here: the governor
/// hands over bytes in whatever quantities a frame's worth of wire time allows, so a CSI
/// sequence routinely arrives split across several ticks — at 300 baud <c>ESC[31m</c>
/// spans five frames. The requirement that a partial sequence never appears as text falls
/// out of the design rather than being bolted on.
/// </para>
/// <para>
/// Feeding the same stream one byte at a time and all at once must produce identical
/// output. That invariant is enforced by test over every Phase 0 capture.
/// </para>
/// </remarks>
public sealed class VtParser : IByteConsumer
{
    private const int MaxParameters = 32;
    private const int MaxIntermediates = 4;
    private const int MaxOscLength = 4096;

    private readonly IVtHandler _handler;
    private readonly int[] _parameters = new int[MaxParameters];
    private readonly char[] _intermediates = new char[MaxIntermediates];
    private readonly char[] _osc = new char[MaxOscLength];

    private State _state = State.Ground;
    private int _parameterCount;
    private int _currentParameter = -1;
    private uint _colonMask;
    private int _intermediateCount;
    private int _oscLength;
    private bool _isPrivate;
    private bool _overflowed;

    // UTF-8 decoding, also resumable mid-character.
    private uint _pendingRune;
    private int _pendingContinuations;

    public VtParser(IVtHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,

        /// <summary>DCS, SOS, PM and APC alike: consumed whole and discarded.</summary>
        StringIgnore,
    }

    /// <summary>Whether the parser is mid-sequence, i.e. holding bytes it has not yet acted on.</summary>
    public bool IsMidSequence => _state != State.Ground || _pendingContinuations > 0;

    public void Consume(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            Feed(b);
        }
    }

    /// <summary>
    /// Returns the parser to the ground state, discarding any partial sequence.
    /// </summary>
    /// <remarks>
    /// Mandatory after a flush that discarded bytes: half an escape sequence otherwise
    /// leaves the parser waiting for a final byte that will never arrive, silently
    /// swallowing everything after it. Never call this after a fast-forward, where nothing
    /// was lost.
    /// </remarks>
    public void Reset()
    {
        _state = State.Ground;
        ClearSequence();
        _pendingRune = 0;
        _pendingContinuations = 0;
    }

    public void Feed(byte b)
    {
        // Anywhere: CAN and SUB abort the current sequence, and ESC restarts one. These
        // take priority over the current state, which is what stops a malformed sequence
        // from wedging the parser.
        switch (b)
        {
            case 0x18 or 0x1A when _state != State.Ground:
                _state = State.Ground;
                ClearSequence();
                return;

            case 0x1B:
                _state = State.Escape;
                ClearSequence();
                return;
        }

        switch (_state)
        {
            case State.Ground:
                Ground(b);
                break;

            case State.Escape:
                Escape(b);
                break;

            case State.EscapeIntermediate:
                EscapeIntermediate(b);
                break;

            case State.CsiEntry:
                CsiEntry(b);
                break;

            case State.CsiParam:
                CsiParam(b);
                break;

            case State.CsiIntermediate:
                CsiIntermediate(b);
                break;

            case State.CsiIgnore:
                CsiIgnore(b);
                break;

            case State.OscString:
                OscString(b);
                break;

            case State.StringIgnore:
                StringIgnore(b);
                break;

            default:
                _state = State.Ground;
                break;
        }
    }

    // ---- Ground: text and C0 controls ---------------------------------------

    private void Ground(byte b)
    {
        if (_pendingContinuations > 0)
        {
            // Mid-character. A continuation byte is 10xxxxxx; anything else means the
            // sequence was truncated, so the partial character becomes a replacement and
            // this byte is reconsidered from scratch.
            if ((b & 0xC0) == 0x80)
            {
                _pendingRune = (_pendingRune << 6) | (uint)(b & 0x3F);
                if (--_pendingContinuations == 0)
                {
                    _handler.Print(IsValidScalar(_pendingRune) ? _pendingRune : 0xFFFD);
                }

                return;
            }

            _pendingContinuations = 0;
            _handler.Print(0xFFFD);
        }

        if (b < 0x20 || b == 0x7F)
        {
            _handler.Execute(b);
            return;
        }

        if (b < 0x80)
        {
            _handler.Print(b);
            return;
        }

        // UTF-8 leading byte.
        if ((b & 0xE0) == 0xC0)
        {
            _pendingRune = (uint)(b & 0x1F);
            _pendingContinuations = 1;
        }
        else if ((b & 0xF0) == 0xE0)
        {
            _pendingRune = (uint)(b & 0x0F);
            _pendingContinuations = 2;
        }
        else if ((b & 0xF8) == 0xF0)
        {
            _pendingRune = (uint)(b & 0x07);
            _pendingContinuations = 3;
        }
        else
        {
            // A stray continuation byte or an invalid lead. specs/terminal-emulation:
            // substitute rather than discard, and carry on from the next byte.
            _handler.Print(0xFFFD);
        }
    }

    private static bool IsValidScalar(uint rune) =>
        rune <= 0x10FFFF && rune is < 0xD800 or > 0xDFFF;

    // ---- Escape -------------------------------------------------------------

    private void Escape(byte b)
    {
        switch (b)
        {
            case < 0x20:
                _handler.Execute(b);
                return;

            case (byte)'[':
                _state = State.CsiEntry;
                return;

            case (byte)']':
                _state = State.OscString;
                _oscLength = 0;
                return;

            case (byte)'P' or (byte)'X' or (byte)'^' or (byte)'_':
                _state = State.StringIgnore;
                return;

            case >= 0x20 and <= 0x2F:
                AddIntermediate((char)b);
                _state = State.EscapeIntermediate;
                return;

            default:
                _state = State.Ground;
                _handler.EscDispatch((char)b, Intermediates);
                return;
        }
    }

    private void EscapeIntermediate(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            AddIntermediate((char)b);
            return;
        }

        _state = State.Ground;
        if (b >= 0x30)
        {
            _handler.EscDispatch((char)b, Intermediates);
        }
    }

    // ---- CSI ----------------------------------------------------------------

    private void CsiEntry(byte b)
    {
        switch (b)
        {
            case < 0x20:
                _handler.Execute(b);
                return;

            case >= 0x3C and <= 0x3F:      // < = > ? — private markers
                _isPrivate = true;
                _state = State.CsiParam;
                return;

            case >= 0x30 and <= 0x39:
            case (byte)';' or (byte)':':
                _state = State.CsiParam;
                CsiParam(b);
                return;

            case >= 0x20 and <= 0x2F:
                AddIntermediate((char)b);
                _state = State.CsiIntermediate;
                return;

            case >= 0x40 and <= 0x7E:
                Dispatch((char)b);
                return;

            default:
                _state = State.CsiIgnore;
                return;
        }
    }

    private void CsiParam(byte b)
    {
        switch (b)
        {
            case < 0x20:
                _handler.Execute(b);
                return;

            case >= 0x30 and <= 0x39:
                var digit = b - '0';
                _currentParameter = _currentParameter < 0
                    ? digit
                    : Math.Min((_currentParameter * 10) + digit, 65535);
                return;

            case (byte)';' or (byte)':':
                PushParameter(colon: b == ':');
                return;

            case >= 0x20 and <= 0x2F:
                AddIntermediate((char)b);
                _state = State.CsiIntermediate;
                return;

            case >= 0x40 and <= 0x7E:
                Dispatch((char)b);
                return;

            default:
                _state = State.CsiIgnore;
                return;
        }
    }

    private void CsiIntermediate(byte b)
    {
        switch (b)
        {
            case < 0x20:
                _handler.Execute(b);
                return;

            case >= 0x20 and <= 0x2F:
                AddIntermediate((char)b);
                return;

            case >= 0x40 and <= 0x7E:
                Dispatch((char)b);
                return;

            default:
                _state = State.CsiIgnore;
                return;
        }
    }

    private void CsiIgnore(byte b)
    {
        if (b < 0x20)
        {
            _handler.Execute(b);
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            // Consumed and dropped: specs/terminal-emulation requires an unrecognised or
            // malformed sequence to vanish rather than print.
            _state = State.Ground;
            ClearSequence();
        }
    }

    private void Dispatch(char final)
    {
        PushParameter(colon: false);
        _state = State.Ground;

        if (!_overflowed)
        {
            _handler.CsiDispatch(
                final,
                _parameters.AsSpan(0, _parameterCount),
                _colonMask,
                Intermediates,
                _isPrivate);
        }

        ClearSequence();
    }

    // ---- OSC ----------------------------------------------------------------

    private void OscString(byte b)
    {
        switch (b)
        {
            case 0x07:                                       // BEL terminates
                _state = State.Ground;
                _handler.OscDispatch(_osc.AsSpan(0, _oscLength));
                _oscLength = 0;
                return;

            case 0x9C:                                       // ST, 8-bit form
                _state = State.Ground;
                _handler.OscDispatch(_osc.AsSpan(0, _oscLength));
                _oscLength = 0;
                return;

            default:
                if (_oscLength < _osc.Length)
                {
                    _osc[_oscLength++] = (char)b;
                }

                return;
        }
    }

    private void StringIgnore(byte b)
    {
        if (b is 0x07 or 0x9C)
        {
            _state = State.Ground;
        }
    }

    // ---- Bookkeeping --------------------------------------------------------

    private ReadOnlySpan<char> Intermediates => _intermediates.AsSpan(0, _intermediateCount);

    private void AddIntermediate(char c)
    {
        if (_intermediateCount < _intermediates.Length)
        {
            _intermediates[_intermediateCount++] = c;
        }
        else
        {
            _overflowed = true;
        }
    }

    private void PushParameter(bool colon)
    {
        if (_parameterCount >= MaxParameters)
        {
            _overflowed = true;
            _currentParameter = -1;
            return;
        }

        if (colon && _parameterCount + 1 < 32)
        {
            _colonMask |= 1u << (_parameterCount + 1);
        }

        _parameters[_parameterCount++] = _currentParameter;
        _currentParameter = -1;
    }

    private void ClearSequence()
    {
        _parameterCount = 0;
        _currentParameter = -1;
        _colonMask = 0;
        _intermediateCount = 0;
        _isPrivate = false;
        _overflowed = false;
        _oscLength = 0;
    }
}
