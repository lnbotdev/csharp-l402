using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LnBot.Models;

namespace LnBot.L402;

/// <summary>
/// HttpClient delegating handler that automatically pays L402 challenges.
/// Detects 402 responses, pays the Lightning invoice via the SDK, caches the token,
/// and retries the request with the L402 Authorization header.
/// </summary>
public class L402DelegatingHandler : DelegatingHandler
{
    private readonly LnBotClient _client;
    private readonly ITokenStore _store;
    private readonly L402ClientOptions _options;
    private readonly Budget _budget;

    public L402DelegatingHandler(LnBotClient client, ITokenStore store, L402ClientOptions options)
    {
        _client = client;
        _store = store;
        _options = options;
        _budget = new Budget(options);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();

        // Buffer content for potential retries (request messages can only be sent once)
        byte[]? contentBytes = null;
        string? contentType = null;
        if (request.Content is not null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentType = request.Content.Headers.ContentType?.ToString();
        }

        // Step 1: Check token cache
        var cached = await _store.GetAsync(url);
        if (cached is not null && !cached.IsExpired)
        {
            var attempt = CloneRequest(request, contentBytes, contentType);
            attempt.Headers.TryAddWithoutValidation("Authorization", cached.Authorization);
            var res = await base.SendAsync(attempt, cancellationToken);
            if ((int)res.StatusCode != 402) return res;
            await _store.DeleteAsync(url);
        }

        // Step 2: Make request without auth
        var fresh = CloneRequest(request, contentBytes, contentType);
        var response = await base.SendAsync(fresh, cancellationToken);
        if ((int)response.StatusCode != 402) return response;

        // Step 3: Parse the 402 challenge
        var wwwAuth = response.Headers.WwwAuthenticate?.ToString();
        if (string.IsNullOrEmpty(wwwAuth) || !wwwAuth.StartsWith("L402 "))
            throw new L402Exception("402 response missing L402 WWW-Authenticate header");

        // Parse body for price info
        var body = await response.Content.ReadFromJsonAsync<L402ChallengeBody>(
            cancellationToken: cancellationToken);
        var price = body?.Price ?? 0;

        // Step 4: Budget checks
        if (price > _options.MaxPrice)
            throw new L402BudgetExceededException(
                $"Price {price} sats exceeds MaxPrice {_options.MaxPrice}");
        _budget.Check(price);

        // Step 5: Pay via SDK
        var payment = await _client.L402.PayAsync(new PayL402Request
        {
            WwwAuthenticate = wwwAuth,
        }, cancellationToken);

        if (payment.Status == "failed")
            throw new L402PaymentFailedException("L402 payment failed");
        if (payment.Authorization is null)
            throw new L402PaymentFailedException("Payment did not return authorization token");

        // Step 6: Cache the token
        await _store.SetAsync(url, new L402Token
        {
            Authorization = payment.Authorization,
            PaidAt = DateTimeOffset.UtcNow,
            ExpiresAt = body?.Expiry is > 0
                ? DateTimeOffset.UtcNow.AddSeconds(body.Expiry.Value)
                : null,
        });
        _budget.Record(price);

        // Step 7: Retry with L402 Authorization
        var retry = CloneRequest(request, contentBytes, contentType);
        retry.Headers.TryAddWithoutValidation("Authorization", payment.Authorization);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage original,
        byte[]? contentBytes,
        string? contentType)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
        {
            if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (contentBytes is not null)
        {
            clone.Content = new ByteArrayContent(contentBytes);
            if (contentType is not null)
                clone.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return clone;
    }
}

internal class L402ChallengeBody
{
    [JsonPropertyName("price")]
    public int? Price { get; set; }

    [JsonPropertyName("expiry")]
    public int? Expiry { get; set; }
}
