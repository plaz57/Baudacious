using Baudacious.Core.Terminal;

namespace Baudacious.Core.Tests;

public class PasteTextTests
{
    // Control characters spelled as code points rather than escapes, so nothing in this
    // file is invisible and nothing is a line terminator to the C# lexer (NEL is).
    private static string C(int codepoint) => ((char)codepoint).ToString();

    private const int Esc = 0x1B;
    private const int Nul = 0x00;
    private const int CtrlC = 0x03;
    private const int CtrlD = 0x04;
    private const int Backspace = 0x08;
    private const int Del = 0x7F;
    private const int Nel = 0x85;
    private const int Csi8Bit = 0x9B;
    private const int LineSeparator = 0x2028;
    private const int NoBreakSpace = 0xA0;

    [Fact]
    public void PlainTextIsUntouched()
    {
        const string Text = "dir /s\tC:\\Users";
        Assert.Same(Text, PasteText.Sanitize(Text));
    }

    [Theory]
    [InlineData("one\r\ntwo", "one\rtwo")]
    [InlineData("one\ntwo\n", "one\rtwo\r")]
    [InlineData("one\rtwo", "one\rtwo")]
    public void LineEndingsBecomeCarriageReturns(string pasted, string sent) =>
        Assert.Equal(sent, PasteText.Sanitize(pasted));

    [Fact]
    public void UnicodeLineEndingsBecomeCarriageReturnsToo()
    {
        Assert.Equal("one\rtwo", PasteText.Sanitize("one" + C(Nel) + "two"));
        Assert.Equal("one\rtwo", PasteText.Sanitize("one" + C(LineSeparator) + "two"));
    }

    [Fact]
    public void TheClosingBracketedPasteMarkerCannotBeSmuggledIn()
    {
        // The whole point: a payload that ends the frame itself, then types a command.
        var payload = "harmless" + C(Esc) + "[201~\rrm -rf ~\r" + C(Esc) + "[200~";

        Assert.Equal("harmless[201~\rrm -rf ~\r[200~", PasteText.Sanitize(payload));
    }

    [Theory]
    [InlineData(CtrlC)]
    [InlineData(CtrlD)]
    [InlineData(Esc)]
    [InlineData(Del)]
    [InlineData(Csi8Bit)]
    [InlineData(Nul)]
    [InlineData(Backspace)]
    public void ControlCharactersAreDropped(int codepoint) =>
        Assert.Equal("ab", PasteText.Sanitize("a" + C(codepoint) + "b"));

    [Fact]
    public void TabsSurvive()
    {
        // Tab is the one C0 control a paste carries on purpose: indented code, TSV.
        Assert.Equal("\tindented\r\tmore", PasteText.Sanitize("\tindented\n\tmore"));
    }

    [Fact]
    public void NonAsciiTextSurvives()
    {
        // Accented letters, box drawing, CJK, and a no-break space: none of it is control.
        var text = "caf" + C(0xE9) + " " + C(0x250C) + C(0x2500) + C(0x2510) + " "
                   + C(0x65E5) + C(0x672C) + C(NoBreakSpace) + "x";

        Assert.Equal(text, PasteText.Sanitize(text));
    }
}
