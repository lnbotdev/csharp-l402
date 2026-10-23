using System.Text.Json;

namespace LnBot.L402.AspNetCore.Tests;

/// <summary>
/// An ln.bot stand-in for server tests: like the real API, verify returns the caveats the
/// challenge was minted with. Tokens look like "L402 mac_hash_N:pre".
/// </summary>
internal sealed class MintingSdk
{
    private readonly Dictionary<string, List<string>?> _minted = new();

    public MockSdkHandler Handler { get; } = new();
    public List<JsonElement> Challenges { get; } = new();

    public MintingSdk()
    {
        Handler.SetHandler("/l402/challenges", request =>
        {
            var body = JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Challenges.Add(body);

            var hash = $"hash_{_minted.Count + 1}";
            _minted[hash] = body.TryGetProperty("caveats", out var caveats) && caveats.ValueKind == JsonValueKind.Array
                ? caveats.EnumerateArray().Select(c => c.GetString()!).ToList()
                : null;

            var invoice = $"lnbc{body.GetProperty("amount").GetInt64() * 10}n1pjtestdata";
            return new
            {
                macaroon = $"mac_{hash}",
                invoice,
                paymentHash = hash,
                expiresAt = "2099-01-01T00:00:00Z",
                wwwAuthenticate = $"L402 macaroon=\"mac_{hash}\", invoice=\"{invoice}\"",
            };
        });

        Handler.SetHandler("/l402/verify", request =>
        {
            var body = JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var authorization = body.GetProperty("authorization").GetString() ?? "";
            var hash = authorization.StartsWith("L402 mac_") && authorization.EndsWith(":pre")
                ? authorization["L402 mac_".Length..^":pre".Length]
                : null;

            if (hash is null || !_minted.TryGetValue(hash, out var caveats))
                return new { valid = false, paymentHash = (string?)null, caveats = (List<string>?)null, error = "invalid token" };
            return new { valid = true, paymentHash = (string?)hash, caveats, error = (string?)null };
        });
    }

    /// <summary>The Authorization header a payer gets for the Nth challenge.</summary>
    public static string TokenFor(int challenge) => $"L402 mac_hash_{challenge}:pre";
}
