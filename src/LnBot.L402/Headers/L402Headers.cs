using System.Text.RegularExpressions;

namespace LnBot.L402;

/// <summary>Parse and format L402 protocol headers.</summary>
public static partial class L402Headers
{
    /// <summary>Parse an L402 Authorization header into macaroon and preimage.</summary>
    public static (string Macaroon, string Preimage)? ParseAuthorization(string? header)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith("L402 "))
            return null;

        var token = header["L402 ".Length..];
        var colonIndex = token.LastIndexOf(':');
        if (colonIndex < 0) return null;

        return (token[..colonIndex], token[(colonIndex + 1)..]);
    }

    /// <summary>Parse a WWW-Authenticate: L402 header into macaroon and invoice.</summary>
    public static (string Macaroon, string Invoice)? ParseChallenge(string? header)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith("L402 "))
            return null;

        var macaroonMatch = MacaroonRegex().Match(header);
        var invoiceMatch = InvoiceRegex().Match(header);
        if (!macaroonMatch.Success || !invoiceMatch.Success) return null;

        return (macaroonMatch.Groups[1].Value, invoiceMatch.Groups[1].Value);
    }

    /// <summary>Format an Authorization header value.</summary>
    public static string FormatAuthorization(string macaroon, string preimage)
        => $"L402 {macaroon}:{preimage}";

    /// <summary>Format a WWW-Authenticate header value.</summary>
    public static string FormatChallenge(string macaroon, string invoice)
        => $"L402 macaroon=\"{macaroon}\", invoice=\"{invoice}\"";

    [GeneratedRegex("macaroon=\"([^\"]+)\"")]
    private static partial Regex MacaroonRegex();

    [GeneratedRegex("invoice=\"([^\"]+)\"")]
    private static partial Regex InvoiceRegex();
}
