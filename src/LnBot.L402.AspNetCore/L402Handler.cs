using System.Globalization;
using LnBot.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LnBot.L402;

/// <summary>
/// Shared L402 verify-or-challenge logic used by middleware, attribute, and endpoint filter.
/// </summary>
/// <remarks>
/// By default each token pays for one request: its challenge carries caveats binding it to the
/// route and price it was issued for, and the paywall remembers it once spent. With
/// <c>reuse</c> a token is a pass instead: bound to the paywall's scope and price, and good
/// until it expires. Single-use tokens and passes carry different caveats, so neither opens
/// the other's routes.
/// </remarks>
internal static class L402Handler
{
    /// <summary>Single-use tokens expire after an hour unless the paywall sets otherwise.</summary>
    internal const int DefaultExpirySeconds = 3600;

    /// <summary>The API takes up to 10 caveats; every token spends two on its binding.</summary>
    internal const int MaxCustomCaveats = 8;

    /// <summary>How long a spent token is remembered when it never expires.</summary>
    private static readonly TimeSpan NoExpiryTtl = TimeSpan.FromDays(30);

    /// <summary>A single-use token: good once, on this path.</summary>
    private const string PathCaveat = "lnbot_path=";
    /// <summary>A pass: good until it expires, on any route under this scope.</summary>
    private const string PassCaveat = "lnbot_pass=";
    /// <summary>What the token was paid: good for that price or less.</summary>
    private const string PriceCaveat = "lnbot_price=";

    /// <summary>Throws if a paywall is given more caveats than fit beside its binding.</summary>
    public static void ValidateCaveats(IReadOnlyCollection<string>? caveats)
    {
        if (caveats?.Count > MaxCustomCaveats)
            throw new ArgumentException(
                $"At most {MaxCustomCaveats} caveats (two bind each token to its paywall and price).",
                nameof(caveats));
    }

    /// <summary>
    /// Attempts to verify an L402 token. If valid, populates HttpContext.Items["L402"] and returns true.
    /// If invalid, spent, or issued for another route, pass or price, issues a 402 challenge and returns false.
    /// </summary>
    /// <param name="passScope">Where a pass is good (the middleware's path prefix); by default the endpoint's route.</param>
    public static async Task<bool> HandleAsync(
        LnBotClient client,
        string walletId,
        HttpContext context,
        int price,
        string? description,
        int? expirySeconds = null,
        List<string>? caveats = null,
        bool reuse = false,
        ISpentTokenStore? spentStore = null,
        string? passScope = null)
    {
        ValidateCaveats(caveats);

        var expiry = expirySeconds ?? (reuse ? null : DefaultExpirySeconds);
        var (kind, where) = reuse ? (PassCaveat, passScope ?? ScopeOf(context)) : (PathCaveat, PathOf(context));
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("L402 "))
        {
            VerifyL402Response? result = null;
            try
            {
                result = await client.Wallet(walletId).L402.VerifyAsync(new VerifyL402Request
                {
                    Authorization = authHeader,
                }, context.RequestAborted);
            }
            catch
            {
                // Verification failed — fall through to issue new challenge
            }

            if (result is { Valid: true, PaymentHash: not null } && Fits(result.Caveats, kind, where, price))
            {
                var accepted = true;
                if (!reuse)
                {
                    // Spend the token. A store failure is a server error, not a reason to bill again.
                    var store = spentStore
                        ?? context.RequestServices?.GetService<ISpentTokenStore>()
                        ?? MemorySpentTokenStore.Shared;
                    var ttl = expiry is > 0 ? TimeSpan.FromSeconds(expiry.Value) : NoExpiryTtl;
                    accepted = await store.ClaimAsync(result.PaymentHash, ttl, context.RequestAborted);
                }

                if (accepted)
                {
                    context.Items["L402"] = result;
                    return true;
                }
            }
        }

        var challenge = await client.Wallet(walletId).L402.CreateChallengeAsync(new CreateL402ChallengeRequest
        {
            Amount = price,
            Description = description,
            ExpirySeconds = expiry,
            Caveats = [.. caveats ?? [], kind + where, PriceCaveat + price.ToString(CultureInfo.InvariantCulture)],
        }, context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.Headers["WWW-Authenticate"] = challenge.WwwAuthenticate;
        context.Response.ContentType = "application/json";

        var body = new Dictionary<string, object?>
        {
            ["type"] = "payment_required",
            ["title"] = "Payment Required",
            ["detail"] = "Pay the included Lightning invoice to access this resource.",
            ["invoice"] = challenge.Invoice,
            ["macaroon"] = challenge.Macaroon,
            ["price"] = price,
            ["unit"] = "satoshis",
            ["description"] = description,
            ["reusable"] = reuse,
        };
        if (expiry is > 0)
            body["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(expiry.Value);

        await context.Response.WriteAsJsonAsync(body, context.RequestAborted);
        return false;
    }

    /// <summary>The request's full path without its query: what the client sends again after paying.</summary>
    private static string PathOf(HttpContext context)
    {
        var path = (context.Request.PathBase + context.Request.Path).Value;
        return string.IsNullOrEmpty(path) ? "/" : path;
    }

    /// <summary>Where a pass is good when no scope is given: the endpoint's route pattern, else the path.</summary>
    private static string ScopeOf(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } pattern }
            ? (pattern.StartsWith('/') ? pattern : "/" + pattern)
            : PathOf(context);

    /// <summary>
    /// Whether a token was issued for this paywall and request. A holder can add caveats to a
    /// macaroon but never remove them, so every binding caveat must hold: each <paramref name="kind"/>
    /// caveat must name <paramref name="where"/>, and every price must cover this request's price.
    /// </summary>
    private static bool Fits(List<string>? caveats, string kind, string where, int price)
    {
        if (caveats is null) return false;

        var places = caveats.Where(c => c.StartsWith(kind, StringComparison.Ordinal))
            .Select(c => c[kind.Length..]).ToList();
        var prices = caveats.Where(c => c.StartsWith(PriceCaveat, StringComparison.Ordinal))
            .Select(c => c[PriceCaveat.Length..]).ToList();

        return places.Count > 0
            && prices.Count > 0
            && places.All(p => p == where)
            && prices.All(p => long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var paid) && paid >= price);
    }
}
