using System.Net;
using System.Text.Json;
using Xunit;

namespace LnBot.L402.Tests;

public class DelegatingHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Helper: creates an LnBotClient backed by a mock HTTP handler.</summary>
    private static (LnBotClient Client, MockSdkHandler SdkHandler) CreateMockSdk()
    {
        var sdkHandler = new MockSdkHandler();
        var httpClient = new HttpClient(sdkHandler) { BaseAddress = new Uri("https://api.ln.bot") };
        var client = new LnBotClient("key_test", new LnBotClientOptions { HttpClient = httpClient });
        return (client, sdkHandler);
    }

    /// <summary>Helper: creates an HttpClient with L402DelegatingHandler wired to a mock inner handler.</summary>
    private static HttpClient CreateTestClient(
        HttpMessageHandler innerHandler,
        LnBotClient lnClient,
        L402ClientOptions? options = null,
        ITokenStore? store = null)
    {
        var handler = new L402DelegatingHandler(lnClient, store ?? new MemoryTokenStore(), options ?? new L402ClientOptions { WalletId = "wal_test" })
        {
            InnerHandler = innerHandler,
        };
        return new HttpClient(handler) { BaseAddress = new Uri("https://protected-api.com") };
    }

    [Fact]
    public async Task NonL402Response_PassedThrough()
    {
        var (lnClient, _) = CreateMockSdk();
        var inner = new MockInnerHandler();
        inner.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":\"free\"}")
        });

        using var http = CreateTestClient(inner, lnClient);
        var res = await http.GetAsync("/data");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("free", body);
    }

    [Fact]
    public async Task L402Challenge_PaysAndRetries()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();

        // SDK: PayAsync returns authorization
        sdkHandler.SetResponse("/l402/pay", new
        {
            authorization = "L402 mac:preimage",
            paymentHash = "hash123",
            preimage = "preimage",
            amount = 10,
            fee = 0,
            paymentNumber = 1,
            status = "settled",
        });

        var inner = new MockInnerHandler();
        var callCount = 0;
        inner.SetHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                // First request: 402
                var challengeRes = new HttpResponseMessage((HttpStatusCode)402)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { price = 10 }, JsonOptions))
                };
                challengeRes.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"lnbc100n1pjtestdata\"");
                return challengeRes;
            }
            // Retry: 200
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":\"premium\"}")
            };
        });

        using var http = CreateTestClient(inner, lnClient);
        var res = await http.GetAsync("/premium");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, callCount);

        // Verify retry had Authorization header
        var lastRequest = inner.LastRequest!;
        Assert.Contains("L402 mac:preimage", lastRequest.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public async Task CachedToken_UsedWithoutPayment()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();

        // Pre-populate cache
        var store = new MemoryTokenStore();
        await store.SetAsync("https://protected-api.com/data", new L402Token
        {
            Authorization = "L402 cached_mac:cached_pre",
            PaidAt = DateTimeOffset.UtcNow,
        });

        var inner = new MockInnerHandler();
        inner.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":\"cached\"}")
        });

        using var http = CreateTestClient(inner, lnClient, store: store);
        var res = await http.GetAsync("/data");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        // Should have sent the cached auth
        var lastRequest = inner.LastRequest!;
        Assert.Contains("L402 cached_mac:cached_pre", lastRequest.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public async Task PriceExceedsMaxPrice_Throws()
    {
        var (lnClient, _) = CreateMockSdk();

        var inner = new MockInnerHandler();
        inner.SetHandler(_ =>
        {
            var res = new HttpResponseMessage((HttpStatusCode)402)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { price = 500 }, JsonOptions))
            };
            res.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"lnbc5000n1pjtestdata\"");
            return res;
        });

        using var http = CreateTestClient(inner, lnClient, new L402ClientOptions { WalletId = "wal_test", MaxPrice = 100 });

        var ex = await Assert.ThrowsAsync<L402BudgetExceededException>(() => http.GetAsync("/expensive"));
        Assert.Contains("500", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public async Task MissingWwwAuthenticate_ThrowsL402Exception()
    {
        var (lnClient, _) = CreateMockSdk();

        var inner = new MockInnerHandler();
        inner.SetResponse(new HttpResponseMessage((HttpStatusCode)402)
        {
            Content = new StringContent("{\"error\":\"pay\"}")
        });

        using var http = CreateTestClient(inner, lnClient);

        await Assert.ThrowsAsync<L402Exception>(() => http.GetAsync("/missing-header"));
    }

    [Fact]
    public async Task PaymentFailed_ThrowsPaymentFailedException()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();

        sdkHandler.SetResponse("/l402/pay", new
        {
            authorization = (string?)null,
            paymentHash = "hash",
            preimage = (string?)null,
            amount = 10,
            fee = (long?)null,
            paymentNumber = 0,
            status = "failed",
        });

        var inner = new MockInnerHandler();
        inner.SetHandler(_ =>
        {
            var res = new HttpResponseMessage((HttpStatusCode)402)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { price = 10 }, JsonOptions))
            };
            res.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"lnbc100n1pjtestdata\"");
            return res;
        });

        using var http = CreateTestClient(inner, lnClient);

        await Assert.ThrowsAsync<L402PaymentFailedException>(() => http.GetAsync("/fail"));
    }

    [Fact]
    public async Task RetryStill402_ThrowsPaymentFailedException()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();

        sdkHandler.SetResponse("/l402/pay", new
        {
            authorization = "L402 mac:pre",
            paymentHash = "hash",
            preimage = "pre",
            amount = 10,
            fee = 0,
            paymentNumber = 1,
            status = "settled",
        });

        var inner = new MockInnerHandler();
        inner.SetHandler(_ =>
        {
            var res = new HttpResponseMessage((HttpStatusCode)402)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { price = 10 }, JsonOptions))
            };
            res.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"lnbc100n1pjtestdata\"");
            return res;
        });

        using var http = CreateTestClient(inner, lnClient);

        var ex = await Assert.ThrowsAsync<L402PaymentFailedException>(() => http.GetAsync("/stuck"));
        Assert.Contains("402 after successful payment", ex.Message);
    }

    /// <summary>A 402 whose body claims a price and whose invoice asks for another.</summary>
    private static HttpResponseMessage Challenge(object body, string invoice)
    {
        var res = new HttpResponseMessage((HttpStatusCode)402)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions)),
        };
        res.Headers.WwwAuthenticate.ParseAdd($"L402 macaroon=\"mac\", invoice=\"{invoice}\"");
        return res;
    }

    private static void SetPayment(MockSdkHandler sdk, int amount) =>
        sdk.SetResponse("/l402/pay", new
        {
            authorization = "L402 mac:pre",
            paymentHash = "hash",
            preimage = "pre",
            amount,
            fee = 0,
            paymentNumber = 1,
            status = "settled",
        });

    [Fact]
    public async Task MaxPrice_CheckedAgainstTheInvoice_NotTheServersClaim()
    {
        var (lnClient, _) = CreateMockSdk();
        var inner = new MockInnerHandler();
        // The body understates the price; the invoice asks for 500.
        inner.SetHandler(_ => Challenge(new { price = 10 }, "lnbc5000n1pjtestdata"));

        using var http = CreateTestClient(inner, lnClient, new L402ClientOptions { WalletId = "wal_test", MaxPrice = 100 });

        var ex = await Assert.ThrowsAsync<L402BudgetExceededException>(() => http.GetAsync("/premium"));
        Assert.Equal("Price 500 sats exceeds MaxPrice 100", ex.Message);
    }

    [Fact]
    public async Task InvoiceWithoutAmount_IsRefused()
    {
        var (lnClient, _) = CreateMockSdk();
        var inner = new MockInnerHandler();
        inner.SetHandler(_ => Challenge(new { price = 10 }, "lnbc1pjtestdata"));

        using var http = CreateTestClient(inner, lnClient);

        var ex = await Assert.ThrowsAsync<L402Exception>(() => http.GetAsync("/premium"));
        Assert.Contains("no amount", ex.Message);
    }

    [Fact]
    public async Task ChallengeWithoutJsonBody_StillPaysTheInvoice()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();
        SetPayment(sdkHandler, 10);
        var inner = new MockInnerHandler();
        var calls = 0;
        inner.SetHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                var res = new HttpResponseMessage((HttpStatusCode)402) { Content = new StringContent("Payment required") };
                res.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"lnbc100n1pjtestdata\"");
                return res;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":\"premium\"}") };
        });

        using var http = CreateTestClient(inner, lnClient);
        var ok = await http.GetAsync("/premium");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task SingleUseToken_IsNotCached()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();
        SetPayment(sdkHandler, 5);
        var inner = new MockInnerHandler();
        var requests = new List<HttpRequestMessage>();
        inner.SetHandler(req =>
        {
            requests.Add(req);
            return requests.Count % 2 == 1
                ? Challenge(new { price = 5, reusable = false }, "lnbc50n1pjtestdata")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        using var http = CreateTestClient(inner, lnClient);
        await http.GetAsync("/premium");
        var second = await http.GetAsync("/premium");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        // Each request: one plain attempt (402) and one paid retry. No stale token in between.
        Assert.Equal(4, requests.Count);
        Assert.False(requests[2].Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task ReusableToken_IsCachedAndSentAgain()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();
        SetPayment(sdkHandler, 5);
        var inner = new MockInnerHandler();
        var requests = new List<HttpRequestMessage>();
        inner.SetHandler(req =>
        {
            requests.Add(req);
            return requests.Count == 1
                ? Challenge(new { price = 5, reusable = true }, "lnbc50n1pjtestdata")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        using var http = CreateTestClient(inner, lnClient);
        await http.GetAsync("/premium");
        await http.GetAsync("/premium");

        Assert.Equal(3, requests.Count);
        Assert.Equal("L402 mac:pre", requests[2].Headers.GetValues("Authorization").Single());
    }
}

/// <summary>Mock handler that simulates the protected upstream API.</summary>
internal class MockInnerHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, HttpResponseMessage>? _handler;
    public HttpRequestMessage? LastRequest { get; private set; }

    public void SetResponse(HttpResponseMessage response) =>
        _handler = _ => response;

    public void SetHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_handler?.Invoke(request)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Mock handler that simulates the ln.bot SDK API.</summary>
internal class MockSdkHandler : HttpMessageHandler
{
    private readonly Dictionary<string, object> _responses = new();

    public void SetResponse(string pathContains, object responseBody) =>
        _responses[pathContains] = responseBody;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        foreach (var (key, body) in _responses)
        {
            if (path.Contains(key))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(body, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        }),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                });
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
