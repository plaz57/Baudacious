using System.Text;
using Baudacious.App;
using Silk.NET.Input;

namespace Baudacious.Core.Tests;

/// <summary>
/// What the keyboard sends to the shell.
/// </summary>
public class KeyEncodingTests
{
    /// <summary>
    /// The rule from specs/input-and-local-commands: apart from Ctrl+C, every Ctrl+letter
    /// belongs to the shell. Checked across the whole alphabet, because a terminal that
    /// quietly eats Ctrl+R or Ctrl+Z is broken in a way its author will not notice.
    /// </summary>
    [Fact]
    public void EveryCtrlLetter_isEncodedAsItsControlCode()
    {
        for (var letter = 0; letter < 26; letter++)
        {
            var key = Key.A + letter;
            var bytes = GlKeyEncoder.Encode(key, control: true, applicationCursorKeys: false);

            Assert.False(bytes.IsEmpty, $"Ctrl+{(char)('A' + letter)} produced nothing");
            Assert.Equal(1, bytes.Length);
            Assert.Equal((byte)(letter + 1), bytes.Span[0]);
        }
    }

    [Theory]
    [InlineData(Key.C, 0x03)]     // interrupt
    [InlineData(Key.D, 0x04)]     // end of input
    [InlineData(Key.Z, 0x1A)]     // suspend
    [InlineData(Key.R, 0x12)]     // reverse history search
    [InlineData(Key.L, 0x0C)]     // clear
    public void TheControlCodesShellsRelyOn(Key key, byte expected)
    {
        var bytes = GlKeyEncoder.Encode(key, control: true, applicationCursorKeys: false);
        Assert.Equal(expected, bytes.Span[0]);
    }

    [Fact]
    public void CursorKeys_followApplicationMode()
    {
        // DECCKM decides between ESC[A and ESC OA. Getting this wrong breaks arrow keys in
        // exactly the programs that bothered to ask for the other form.
        var normal = GlKeyEncoder.Encode(Key.Up, control: false, applicationCursorKeys: false);
        var application = GlKeyEncoder.Encode(Key.Up, control: false, applicationCursorKeys: true);

        Assert.Equal("\u001b[A", Encoding.ASCII.GetString(normal.Span));
        Assert.Equal("\u001bOA", Encoding.ASCII.GetString(application.Span));
    }

    [Theory]
    [InlineData(Key.Enter, 0x0D)]
    [InlineData(Key.Backspace, 0x7F)]
    [InlineData(Key.Tab, 0x09)]
    [InlineData(Key.Escape, 0x1B)]
    public void TheSingleByteKeys(Key key, byte expected)
    {
        var bytes = GlKeyEncoder.Encode(key, control: false, applicationCursorKeys: false);
        Assert.Equal(expected, bytes.Span[0]);
    }

    [Theory]
    [InlineData(Key.Delete, "\u001b[3~")]
    [InlineData(Key.PageUp, "\u001b[5~")]
    [InlineData(Key.F5, "\u001b[15~")]
    [InlineData(Key.F1, "\u001bOP")]
    public void TheSequenceKeys(Key key, string expected)
    {
        var bytes = GlKeyEncoder.Encode(key, control: false, applicationCursorKeys: false);
        Assert.Equal(expected, Encoding.ASCII.GetString(bytes.Span));
    }

    /// <summary>
    /// Unbound Alt chords go to the shell as ESC + key, which is what lets the local
    /// command layer live on Alt without taking anything the shell might want.
    /// </summary>
    [Fact]
    public void AltChords_haveAMetaByte()
    {
        Assert.Equal((byte)'q', GlKeyEncoder.MetaByte(Key.Q));
        Assert.Equal((byte)'a', GlKeyEncoder.MetaByte(Key.A));
        Assert.Equal((byte)'7', GlKeyEncoder.MetaByte(Key.Number7));
        Assert.Equal((byte)'.', GlKeyEncoder.MetaByte(Key.Period));
    }

    [Fact]
    public void KeysWithNoTextForm_haveNoMetaByte()
    {
        Assert.Null(GlKeyEncoder.MetaByte(Key.F1));
        Assert.Null(GlKeyEncoder.MetaByte(Key.Up));
        Assert.Null(GlKeyEncoder.MetaByte(Key.ShiftLeft));
    }

    [Fact]
    public void UnknownKeys_sendNothing()
    {
        Assert.True(GlKeyEncoder.Encode(Key.Unknown, control: false, applicationCursorKeys: false).IsEmpty);
        Assert.True(GlKeyEncoder.Encode(Key.ShiftLeft, control: false, applicationCursorKeys: false).IsEmpty);
    }
}
