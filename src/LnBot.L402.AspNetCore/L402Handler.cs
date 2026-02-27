using LnBot.Models;
using Microsoft.AspNetCore.Http;

namespace LnBot.L402;

/// <summary>
/// Shared L402 verify-or-challenge logic used by middleware, attribute, and endpoint filter.
/// </summary>
internal static class L402Handler
{
    /// <summary>
    /// Attempts to verify an L402 token. If valid, populates HttpContext.Items["L402"] and returns true.
    /// If invalid or missing, issues a 402 challenge and returns false.
    /// </summary>
    public static async Task<bool> HandleAsync(
        LnBotClient client,
        HttpContext context,
        int price,
        string? description,
        int? expirySeconds = null,
        List<string>? caveats = null)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("L402 "))
        {
            try
            {
                var result = await client.L402.VerifyAsync(new VerifyL402Request
                {
                    Authorization = authHeader,
                }, context.RequestAborted);

                if (result.Valid)
                {
                    context.Items["L402"] = result;
                    return true;
                }
            }
            catch
            {
                // Verification failed — fall through to issue new challenge
            }
        }

        var challenge = await client.L402.CreateChallengeAsync(new CreateL402ChallengeRequest
        {
            Amount = price,
            Description = description,
            ExpirySeconds = expirySeconds,
            Caveats = caveats,
        }, context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.Headers["WWW-Authenticate"] = challenge.WwwAuthenticate;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "payment_required",
            title = "Payment Required",
            detail = "Pay the included Lightning invoice to access this resource.",
            invoice = challenge.Invoice,
            macaroon = challenge.Macaroon,
            price,
            unit = "satoshis",
            description,
        }, context.RequestAborted);

        return false;
    }
}
