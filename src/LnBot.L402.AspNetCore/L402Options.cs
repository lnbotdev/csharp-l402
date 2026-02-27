using Microsoft.AspNetCore.Http;

namespace LnBot.L402;

/// <summary>Server-side L402 paywall options.</summary>
public class L402Options
{
    /// <summary>Static price in sats per request.</summary>
    public int Price { get; set; }

    /// <summary>Dynamic pricing callback. Takes precedence over Price if set.</summary>
    public Func<HttpContext, Task<int>>? PriceFactory { get; set; }

    /// <summary>Invoice memo / description.</summary>
    public string? Description { get; set; }
}
