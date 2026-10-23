using Microsoft.AspNetCore.Http;

namespace LnBot.L402;

/// <summary>Server-side L402 paywall options.</summary>
public class L402Options
{
    /// <summary>LnBot wallet ID to use for L402 challenges and verification.</summary>
    public string WalletId { get; set; } = "";

    /// <summary>Static price in sats per request.</summary>
    public int Price { get; set; }

    /// <summary>Dynamic pricing callback. Takes precedence over Price if set.</summary>
    public Func<HttpContext, Task<int>>? PriceFactory { get; set; }

    /// <summary>Invoice memo / description.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Token lifetime in seconds. Defaults to 3600 while tokens are single-use;
    /// with <see cref="Reuse"/> a token has no expiry unless you set one.
    /// </summary>
    public int? ExpirySeconds { get; set; }

    /// <summary>Macaroon caveats to attach (at most 8: two more bind each token to its paywall and price).</summary>
    public List<string>? Caveats { get; set; }

    /// <summary>
    /// False (the default): a token pays for one request. It works once, on the route and at
    /// the price it was issued for. True: a token is a pass. It keeps working until it expires,
    /// on any route under this paywall's path; single-use tokens, and passes for another path
    /// or a lower price, don't open it.
    /// </summary>
    public bool Reuse { get; set; }

    /// <summary>
    /// Where spent tokens are remembered. Defaults to an <see cref="ISpentTokenStore"/> from DI,
    /// else this process's memory. When the API runs on several instances, use one store they
    /// share, so a token spent on one can't be spent again on another.
    /// </summary>
    public ISpentTokenStore? SpentStore { get; set; }
}
