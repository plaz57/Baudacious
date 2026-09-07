using Baudacious.App;
using Baudacious.App.Config;
using Baudacious.Core.Commands;
using Baudacious.Render;

namespace Baudacious.Core.Tests;

/// <summary>
/// Everything Baudacious writes on its own screen has to exist in the font.
/// </summary>
/// <remarks>
/// The grid is CP437 and the atlas has 256 slots, so a character outside it is drawn as a
/// solid square. That is the right answer for shell output nobody controls; it is the wrong
/// answer for our own chrome, where an em dash typed in a help line silently becomes a
/// block. This catches that at build time rather than on screen.
/// </remarks>
public class ChromeGlyphTests
{
    public static TheoryData<string, string> Chrome()
    {
        var data = new TheoryData<string, string>();

        foreach (var line in Help.For(ConfigStore.DefaultDirectory()))
        {
            data.Add("help overlay", line);
        }

        foreach (var line in CommandVocabulary.Usage)
        {
            data.Add("command usage", line);
        }

        foreach (var verb in CommandVocabulary.Verbs)
        {
            data.Add("command verb", verb);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Chrome))]
    public void EveryCharacterOfOurOwnChromeIsInTheFont(string where, string line)
    {
        foreach (var character in line)
        {
            Assert.True(
                Cp437.IndexOf(character) != Cp437.Substitute || character == '■',
                $"{where}: U+{(int)character:X4} '{character}' is not in CP437, so it would " +
                $"draw as a solid square — in: {line}");
        }
    }

    [Fact]
    public void TheGuardIsNotVacuous()
    {
        // The character that prompted this test. If CP437 ever grew one, the guard above
        // would be checking nothing.
        Assert.Equal(Cp437.Substitute, Cp437.IndexOf('—'));
    }
}
