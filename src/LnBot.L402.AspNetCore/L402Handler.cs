using System.Globalization;
using LnBot.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LnBot.L402;

/// <summary>
/// Shared L402 verify-or-challenge logic used by middleware, attribute, and endpoint filter.
/// </summary>
/// <remarks>
/// By default each token pays for one request: its challenge carries caveats binding it to the
/// route and price it was issued for, and the paywall remembers it once spent. With
/// <c>reuse</c> a token works until it expires instead.
/// </remarks>
internal static class L402Handler
{
    /// <summary>Single-use tokens expire after an hour unless the paywall sets otherwise.</summary>
    internal const int DefaultExpirySeconds = 3600;

    /// <summary>The API takes up to 10 caveats; single-use tokens spend two on their binding.</summary>
    internal const int MaxCustomCaveats = 8;

    /// <summary>How long a spent token is remembered when it never expires.</summary>
    private static readonly TimeSpan NoExpiryTtl = TimeSpan.FromDays(30);

    private const string PathCaveat = "lnbot_path=";
    private const string PriceCaveat = "lnbot_price=";

    /// <summary>Throws if a single-use paywall is given more caveats than fit beside its binding.</summary>
    public static void ValidateCaveats(IReadOnlyCollection<string>? caveats, bool reuse)
    {
        if (!reuse && caveats?.Count > MaxCustomCaveats)
            throw new ArgumentException(
                $"At most {MaxCustomCaveats} caveats while tokens are single-use (two bind each token to its route and price).",
                nameof(caveats));
    }

    /// <summary>
    /// Attempts to verify an L402 token. If valid, populates HttpContext.Items["L402"] and returns true.
    /// If invalid, spent, or issued for another route or price, issues a 402 challenge and returns false.
    /// </summary>
    public static async Task<bool> HandleAsync(
        LnBotClient client,
        string walletId,
        HttpContext context,
        int price,
        string? description,
        int? expirySeconds = null,
        List<string>? caveats = null,
        bool reuse = false,
        ISpentTokenStore? spentStore = null)
    {
        ValidateCaveats(caveats, reuse);

        var expiry = expirySeconds ?? (reuse ? null : DefaultExpirySeconds);
        var path = PathOf(context);
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

            if (result is { Valid: true, PaymentHash: not null })
            {
                var accepted = reuse;
                if (!reuse && Fits(result.Caveats, path, price))
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
            Caveats = reuse
                ? caveats
                : [.. caveats ?? [], PathCaveat + path, PriceCaveat + price.ToString(CultureInfo.InvariantCulture)],
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

    /// <summary>
    /// Whether a token was issued for this request. A holder can add caveats to a macaroon but
    /// never remove them, so every binding caveat must hold: the path must match, and every price
    /// must cover this request's price.
    /// </summary>
    private static bool Fits(List<string>? caveats, string path, int price)
    {
        if (caveats is null) return false;

        var paths = caveats.Where(c => c.StartsWith(PathCaveat, StringComparison.Ordinal))
            .Select(c => c[PathCaveat.Length..]).ToList();
        var prices = caveats.Where(c => c.StartsWith(PriceCaveat, StringComparison.Ordinal))
            .Select(c => c[PriceCaveat.Length..]).ToList();

        return paths.Count > 0
            && prices.Count > 0
            && paths.All(p => p == path)
            && prices.All(p => long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var paid) && paid >= price);
    }
}
