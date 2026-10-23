using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LnBot.L402.AspNetCore.Tests;

/// <summary>One token, one request: the default since 1.1.</summary>
public class SingleUseTests
{
    private static async Task<(IHost Host, HttpClient Client, MintingSdk Sdk)> StartAsync(L402Options options)
    {
        var sdk = new MintingSdk();
        var lnClient = new LnBotClient("uk_test", new LnBotClientOptions
        {
            HttpClient = new HttpClient(sdk.Handler) { BaseAddress = new Uri("https://api.ln.bot") },
        });

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(lnClient);
                    services.AddSingleton<ISpentTokenStore>(new MemorySpentTokenStore());
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseL402Paywall("/api/premium", options);
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/premium/data", () => Results.Ok(new { data = "premium" }));
                        endpoints.MapGet("/api/premium/report", () => Results.Ok(new { data = "report" }));
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return (host, host.GetTestClient(), sdk);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? authorization = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Challenge_BindsTheTokenToItsRouteAndPrice()
    {
        var (host, client, sdk) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10 });

        var res = await GetAsync(client, "/api/premium/data");

        Assert.Equal((HttpStatusCode)402, res.StatusCode);
        var body = await JsonAsync(res);
        Assert.False(body.GetProperty("reusable").GetBoolean());
        Assert.True(body.TryGetProperty("expiresAt", out _));

        var challenge = Assert.Single(sdk.Challenges);
        Assert.Equal(3600, challenge.GetProperty("expirySeconds").GetInt32());
        Assert.Equal(
            new string?[] { "lnbot_path=/api/premium/data", "lnbot_price=10" },
            challenge.GetProperty("caveats").EnumerateArray().Select(c => c.GetString()).ToArray());

        await host.StopAsync();
    }

    [Fact]
    public async Task Token_WorksOnce()
    {
        var (host, client, _) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10 });

        await GetAsync(client, "/api/premium/data");
        var token = MintingSdk.TokenFor(1);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/premium/data", token)).StatusCode);
        Assert.Equal((HttpStatusCode)402, (await GetAsync(client, "/api/premium/data", token)).StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task Token_DoesNotOpenAnotherRoute()
    {
        var (host, client, _) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10 });

        await GetAsync(client, "/api/premium/data");
        var token = MintingSdk.TokenFor(1);

        Assert.Equal((HttpStatusCode)402, (await GetAsync(client, "/api/premium/report", token)).StatusCode);
        // Refused there, it still works where it was bought.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/premium/data", token)).StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task Token_PaidAtALowerPrice_IsRefused()
    {
        var price = 10;
        var (host, client, _) = await StartAsync(new L402Options
        {
            WalletId = "wal_test",
            PriceFactory = _ => Task.FromResult(price),
        });

        await GetAsync(client, "/api/premium/data");
        price = 50;

        Assert.Equal((HttpStatusCode)402, (await GetAsync(client, "/api/premium/data", MintingSdk.TokenFor(1))).StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task ValidToken_WithoutBinding_IsRefused()
    {
        var (host, client, sdk) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10 });
        // A token ln.bot accepts but that carries no binding (minted elsewhere on the wallet, or by 1.0).
        sdk.Handler.SetResponse("/l402/verify", new
        {
            valid = true,
            paymentHash = "hash_unbound",
            caveats = new List<string> { "expiry=2099" },
            error = (string?)null,
        });

        Assert.Equal((HttpStatusCode)402, (await GetAsync(client, "/api/premium/data", "L402 mac:pre")).StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task Reuse_LetsATokenWorkUntilItExpires()
    {
        var (host, client, sdk) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10, Reuse = true });

        var challenge = await GetAsync(client, "/api/premium/data");
        var body = await JsonAsync(challenge);
        Assert.True(body.GetProperty("reusable").GetBoolean());
        Assert.False(body.TryGetProperty("expiresAt", out _));
        var minted = Assert.Single(sdk.Challenges);
        // A pass is bound to the paywall's prefix and price, not to one path.
        Assert.Equal(
            new string?[] { "lnbot_pass=/api/premium", "lnbot_price=10" },
            minted.GetProperty("caveats").EnumerateArray().Select(c => c.GetString()).ToArray());

        var token = MintingSdk.TokenFor(1);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/premium/data", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/premium/data", token)).StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task Pass_RefusesSingleUseTokensAndCheaperPasses()
    {
        var (host, client, sdk) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 500, Reuse = true });

        foreach (var caveats in new[]
        {
            new List<string> { "lnbot_path=/api/premium/data", "lnbot_price=500" }, // a single-use token
            new List<string> { "lnbot_pass=/api/premium", "lnbot_price=10" },       // a cheaper pass
            new List<string> { "lnbot_pass=/api/other", "lnbot_price=500" },        // a pass for elsewhere
        })
        {
            sdk.Handler.SetResponse("/l402/verify", new { valid = true, paymentHash = "h", caveats, error = (string?)null });
            Assert.Equal((HttpStatusCode)402, (await GetAsync(client, "/api/premium/data", "L402 mac:pre")).StatusCode);
        }

        sdk.Handler.SetResponse("/l402/verify", new
        {
            valid = true,
            paymentHash = "h",
            caveats = new List<string> { "lnbot_pass=/api/premium", "lnbot_price=500" },
            error = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/premium/report", "L402 mac:pre")).StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task SingleUse_RefusesAPass()
    {
        var (host, client, sdk) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10 });
        sdk.Handler.SetResponse("/l402/verify", new
        {
            valid = true,
            paymentHash = "h",
            caveats = new List<string> { "lnbot_pass=/api/premium", "lnbot_price=500" },
            error = (string?)null,
        });

        Assert.Equal((HttpStatusCode)402, (await GetAsync(client, "/api/premium/data", "L402 mac:pre")).StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task SpentStore_FromOptions_IsUsed()
    {
        var store = new RecordingStore();
        var (host, client, _) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10, ExpirySeconds = 120, SpentStore = store });

        await GetAsync(client, "/api/premium/data");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/premium/data", MintingSdk.TokenFor(1))).StatusCode);

        var (hash, ttl) = Assert.Single(store.Claims);
        Assert.Equal("hash_1", hash);
        Assert.Equal(TimeSpan.FromSeconds(120), ttl);

        await host.StopAsync();
    }

    [Fact]
    public async Task SpentStoreFailure_IsAServerError_NotANewBill()
    {
        var (host, client, sdk) = await StartAsync(new L402Options { WalletId = "wal_test", Price = 10, SpentStore = new FailingStore() });

        await GetAsync(client, "/api/premium/data");

        await Assert.ThrowsAnyAsync<Exception>(() => GetAsync(client, "/api/premium/data", MintingSdk.TokenFor(1)));
        Assert.Single(sdk.Challenges);

        await host.StopAsync();
    }

    [Fact]
    public void TooManyCaveats_AreRefusedUpFront()
    {
        var caveats = Enumerable.Range(0, 9).Select(i => $"k{i}=v").ToList();

        Assert.Throws<ArgumentException>(() => new L402EndpointFilter("wal_test", 10, null, null, caveats));
        Assert.Throws<ArgumentException>(() => new L402EndpointFilter("wal_test", 10, null, null, caveats, reuse: true));
        _ = new L402EndpointFilter("wal_test", 10, null, null, caveats.Take(8).ToList());
    }

    [Fact]
    public async Task MemorySpentTokenStore_ForgetsAHashOnceItsTokenHasExpired()
    {
        var time = new ManualTime();
        var store = new MemorySpentTokenStore(time);

        Assert.True(await store.ClaimAsync("h", TimeSpan.FromSeconds(60)));
        Assert.False(await store.ClaimAsync("h", TimeSpan.FromSeconds(60)));

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(await store.ClaimAsync("h", TimeSpan.FromSeconds(60)));
    }

    private sealed class RecordingStore : ISpentTokenStore
    {
        public List<(string Hash, TimeSpan Ttl)> Claims { get; } = new();

        public Task<bool> ClaimAsync(string paymentHash, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            Claims.Add((paymentHash, ttl));
            return Task.FromResult(true);
        }
    }

    private sealed class FailingStore : ISpentTokenStore
    {
        public Task<bool> ClaimAsync(string paymentHash, TimeSpan ttl, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("redis down");
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
