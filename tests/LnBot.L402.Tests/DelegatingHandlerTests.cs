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
        var handler = new L402DelegatingHandler(lnClient, store ?? new MemoryTokenStore(), options ?? new L402ClientOptions())
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
        sdkHandler.SetResponse("/v1/l402/pay", new
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
                challengeRes.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"inv\"");
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
            res.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"inv\"");
            return res;
        });

        using var http = CreateTestClient(inner, lnClient, new L402ClientOptions { MaxPrice = 100 });

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

        sdkHandler.SetResponse("/v1/l402/pay", new
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
            res.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"inv\"");
            return res;
        });

        using var http = CreateTestClient(inner, lnClient);

        await Assert.ThrowsAsync<L402PaymentFailedException>(() => http.GetAsync("/fail"));
    }

    [Fact]
    public async Task RetryStill402_ThrowsPaymentFailedException()
    {
        var (lnClient, sdkHandler) = CreateMockSdk();

        sdkHandler.SetResponse("/v1/l402/pay", new
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
            res.Headers.WwwAuthenticate.ParseAdd("L402 macaroon=\"mac\", invoice=\"inv\"");
            return res;
        });

        using var http = CreateTestClient(inner, lnClient);

        var ex = await Assert.ThrowsAsync<L402PaymentFailedException>(() => http.GetAsync("/stuck"));
        Assert.Contains("402 after successful payment", ex.Message);
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
