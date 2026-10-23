using System.Numerics;
using System.Text.RegularExpressions;

namespace LnBot.L402;

/// <summary>Reads what a BOLT11 invoice asks for.</summary>
public static partial class Bolt11
{
    /// <summary>
    /// The amount a BOLT11 invoice asks for, in sats (rounded up), read from the invoice's
    /// human-readable part. Returns null for an invoice without an amount.
    /// </summary>
    /// <remarks>
    /// The L402 client checks prices with this, not with what a server says about itself:
    /// the invoice is what gets paid.
    /// </remarks>
    /// <exception cref="L402Exception">The string isn't a BOLT11 invoice.</exception>
    public static long? AmountSats(string invoice)
    {
        var s = invoice.Trim().ToLowerInvariant();
        if (s.StartsWith("lightning:", StringComparison.Ordinal))
            s = s["lightning:".Length..];

        // The data part's alphabet has no "1", so the last one is the separator.
        var separator = s.LastIndexOf('1');
        var match = separator < 0 ? Match.Empty : HrpRegex().Match(s[..separator]);
        if (!match.Success)
            throw new L402Exception("Not a BOLT11 invoice");

        var digits = match.Groups[1].Value;
        if (digits.Length == 0) return null;

        var amount = BigInteger.Parse(digits);
        var msat = match.Groups[2].Value switch
        {
            "m" => amount * 100_000_000,
            "u" => amount * 100_000,
            "n" => amount * 100,
            // A pico-bitcoin is a tenth of a millisatoshi; BOLT11 requires whole millisatoshis.
            "p" => amount % 10 == 0 ? amount / 10 : throw new L402Exception("Invalid BOLT11 amount"),
            _ => amount * 100_000_000_000, // whole bitcoin
        };

        var sats = (msat + 999) / 1000;
        if (sats > long.MaxValue)
            throw new L402Exception("Invalid BOLT11 amount");
        return (long)sats;
    }

    [GeneratedRegex("^ln(?:bcrt|bc|tbs|tb|sb)([0-9]*)([munp]?)$")]
    private static partial Regex HrpRegex();
}
