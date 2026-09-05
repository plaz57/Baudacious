using Baudacious.Core.Baud;

namespace Baudacious.Core.Tests;

public class FramingTests
{
    [Theory]
    [InlineData(8, Parity.None, 1, 10)]
    [InlineData(7, Parity.Even, 1, 10)]
    [InlineData(8, Parity.None, 2, 11)]
    [InlineData(8, Parity.Even, 1, 11)]
    public void BitsPerCharacter_countsStartDataParityAndStop(int data, Parity parity, int stop, int expected) =>
        Assert.Equal(expected, new Framing(data, parity, stop).BitsPerCharacter);

    [Fact]
    public void SameBaud_differentFraming_givesDifferentCharacterRate()
    {
        // The reason framing is a first-class setting rather than a detail.
        Assert.Equal(120.0, LineRate.Of(1200, Framing.N81).BytesPerSecond, 6);
        Assert.Equal(1200 / 11.0, LineRate.Of(1200, Framing.N82).BytesPerSecond, 6);
    }

    [Fact]
    public void OneHundredTenBaud_defaultsToTwoStopBits_givingExactlyTenCharactersPerSecond()
    {
        // An ASR-33 ran 8N2, and 110/11 = 10 cps exactly. That is why the framing existed.
        var rate = LineRate.Of(110);
        Assert.Equal(Framing.N82, rate.Framing);
        Assert.Equal(10.0, rate.BytesPerSecond, 9);
    }

    [Fact]
    public void EveryOtherStandardRate_defaultsTo8N1()
    {
        foreach (var baud in LineRate.StandardBauds.Where(b => b != 110))
        {
            Assert.Equal(Framing.N81, LineRate.Of(baud).Framing);
        }
    }

    [Theory]
    [InlineData("8N1", 8, Parity.None, 1)]
    [InlineData("7e1", 7, Parity.Even, 1)]
    [InlineData("8O2", 8, Parity.Odd, 2)]
    public void TryParse_readsConventionalNotation(string text, int data, Parity parity, int stop)
    {
        Assert.True(Framing.TryParse(text, out var framing));
        Assert.Equal(new Framing(data, parity, stop), framing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8N")]
    [InlineData("8X1")]
    [InlineData("9N1")]
    [InlineData("8N3")]
    public void TryParse_rejectsNonsense(string? text) => Assert.False(Framing.TryParse(text, out _));

    [Fact]
    public void ToString_roundTrips()
    {
        foreach (var framing in new[] { Framing.N81, Framing.E71, Framing.N82, Framing.E81 })
        {
            Assert.True(Framing.TryParse(framing.ToString(), out var parsed));
            Assert.Equal(framing, parsed);
        }
    }
}

public class LineRateTests
{
    [Fact]
    public void Unlimited_isDistinctFromAnyRate()
    {
        Assert.True(LineRate.Unlimited.IsUnlimited);
        Assert.False(LineRate.Of(115200).IsUnlimited);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(LineRate.MaxBaud + 1)]
    public void Of_rejectsOutOfRange(int baud) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LineRate.Of(baud));

    [Theory]
    [InlineData("1200", 1200, 10)]
    [InlineData("1200 8N2", 1200, 11)]
    [InlineData("  2400,7E1 ", 2400, 10)]
    [InlineData("110", 110, 11)]
    public void TryParse_readsRateAndOptionalFraming(string text, int baud, int bits)
    {
        Assert.True(LineRate.TryParse(text, out var rate));
        Assert.Equal(baud, rate.Baud);
        Assert.Equal(bits, rate.Framing.BitsPerCharacter);
    }

    [Theory]
    [InlineData("unlimited")]
    [InlineData("MAX")]
    public void TryParse_acceptsUnlimited(string text)
    {
        Assert.True(LineRate.TryParse(text, out var rate));
        Assert.True(rate.IsUnlimited);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("fast")]
    [InlineData("1200 8Q1")]
    [InlineData("")]
    public void TryParse_rejectsNonsense(string text) => Assert.False(LineRate.TryParse(text, out _));
}
