using Xunit;

namespace LnBot.L402.Tests;

public class Bolt11Tests
{
    [Theory]
    [InlineData("lnbc1m1pjtestdata", 100_000)] // 1 mBTC
    [InlineData("lnbc2500u1pjtestdata", 250_000)]
    [InlineData("lnbc10u1pjtestdata", 1_000)]
    [InlineData("lnbc100n1pjtestdata", 10)]
    [InlineData("lnbc11pjtestdata", 100_000_000)] // whole bitcoin
    [InlineData("lnbc10p1pjtestdata", 1)] // one millisatoshi, rounded up
    [InlineData("lnbc15n1pjtestdata", 2)] // 1.5 sats, rounded up
    [InlineData("lntb100n1pjtestdata", 10)]
    [InlineData("lntbs100n1pjtestdata", 10)]
    [InlineData("lnbcrt100n1pjtestdata", 10)]
    [InlineData("LNBC100N1PJTESTDATA", 10)]
    [InlineData("lightning:lnbc100n1pjtestdata", 10)]
    public void AmountSats_ReadsTheHumanReadablePart(string invoice, long sats) =>
        Assert.Equal(sats, Bolt11.AmountSats(invoice));

    [Fact]
    public void AmountSats_NullWithoutAnAmount() =>
        Assert.Null(Bolt11.AmountSats("lnbc1pjtestdata"));

    [Theory]
    [InlineData("not-an-invoice")]
    [InlineData("lnurl1dp68gurn8ghj7")]
    [InlineData("lnbc1x1pjtestdata")]
    [InlineData("lnbc15p1pjtestdata")] // not whole millisatoshis
    public void AmountSats_RejectsWhatIsntBolt11(string invoice) =>
        Assert.Throws<L402Exception>(() => Bolt11.AmountSats(invoice));
}
