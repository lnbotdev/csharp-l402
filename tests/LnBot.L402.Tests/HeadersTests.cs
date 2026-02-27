using Xunit;

namespace LnBot.L402.Tests;

public class HeadersTests
{
    [Fact]
    public void ParseAuthorization_ValidHeader_ReturnsMacaroonAndPreimage()
    {
        var result = L402Headers.ParseAuthorization("L402 abc123:def456");
        Assert.NotNull(result);
        Assert.Equal("abc123", result.Value.Macaroon);
        Assert.Equal("def456", result.Value.Preimage);
    }

    [Fact]
    public void ParseAuthorization_ColonsInPreimage_UsesLastColon()
    {
        var result = L402Headers.ParseAuthorization("L402 mac:with:colons:preimage");
        Assert.NotNull(result);
        Assert.Equal("mac:with:colons", result.Value.Macaroon);
        Assert.Equal("preimage", result.Value.Preimage);
    }

    [Fact]
    public void ParseAuthorization_Base64Padding_Preserved()
    {
        var result = L402Headers.ParseAuthorization("L402 YWJjMTIz==:abcdef01");
        Assert.NotNull(result);
        Assert.Equal("YWJjMTIz==", result.Value.Macaroon);
        Assert.Equal("abcdef01", result.Value.Preimage);
    }

    [Fact]
    public void ParseAuthorization_NonL402Header_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseAuthorization("Bearer token123"));
    }

    [Fact]
    public void ParseAuthorization_MissingColon_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseAuthorization("L402 nocolonhere"));
    }

    [Fact]
    public void ParseAuthorization_Empty_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseAuthorization(""));
    }

    [Fact]
    public void ParseAuthorization_Null_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseAuthorization(null));
    }

    [Fact]
    public void ParseChallenge_ValidHeader_ReturnsMacaroonAndInvoice()
    {
        var result = L402Headers.ParseChallenge("L402 macaroon=\"abc123\", invoice=\"lnbc1...\"");
        Assert.NotNull(result);
        Assert.Equal("abc123", result.Value.Macaroon);
        Assert.Equal("lnbc1...", result.Value.Invoice);
    }

    [Fact]
    public void ParseChallenge_ExtraWhitespace_StillParses()
    {
        var result = L402Headers.ParseChallenge("L402 macaroon=\"mac123\",  invoice=\"lnbc500\"");
        Assert.NotNull(result);
        Assert.Equal("mac123", result.Value.Macaroon);
        Assert.Equal("lnbc500", result.Value.Invoice);
    }

    [Fact]
    public void ParseChallenge_NonL402_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseChallenge("Basic realm=test"));
    }

    [Fact]
    public void ParseChallenge_MissingMacaroon_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseChallenge("L402 invoice=\"lnbc1...\""));
    }

    [Fact]
    public void ParseChallenge_MissingInvoice_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseChallenge("L402 macaroon=\"abc123\""));
    }

    [Fact]
    public void ParseChallenge_Empty_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseChallenge(""));
    }

    [Fact]
    public void ParseChallenge_Null_ReturnsNull()
    {
        Assert.Null(L402Headers.ParseChallenge(null));
    }

    [Fact]
    public void FormatAuthorization_FormatsCorrectly()
    {
        Assert.Equal("L402 abc:def", L402Headers.FormatAuthorization("abc", "def"));
    }

    [Fact]
    public void FormatChallenge_FormatsCorrectly()
    {
        Assert.Equal(
            "L402 macaroon=\"abc\", invoice=\"lnbc1\"",
            L402Headers.FormatChallenge("abc", "lnbc1"));
    }

    [Fact]
    public void Roundtrip_Authorization()
    {
        var formatted = L402Headers.FormatAuthorization("mac_data", "pre_hex");
        var parsed = L402Headers.ParseAuthorization(formatted);
        Assert.NotNull(parsed);
        Assert.Equal("mac_data", parsed.Value.Macaroon);
        Assert.Equal("pre_hex", parsed.Value.Preimage);
    }

    [Fact]
    public void Roundtrip_Challenge()
    {
        var formatted = L402Headers.FormatChallenge("mac_base64", "lnbc1pvjluez");
        var parsed = L402Headers.ParseChallenge(formatted);
        Assert.NotNull(parsed);
        Assert.Equal("mac_base64", parsed.Value.Macaroon);
        Assert.Equal("lnbc1pvjluez", parsed.Value.Invoice);
    }
}
