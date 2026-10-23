using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LnBot.L402.AspNetCore.Tests;

public class IntegrationTests
{
    /// <summary>A server with a pay-per-request route and a reusable pass, and an auto-paying client.</summary>
    private static async Task<(IHost Host, HttpClient Client, Func<int> Payments)> StartAsync()
    {
        // ── Server-side SDK: mints challenges, verifies tokens with their caveats ──
        var serverSdk = new MintingSdk();
        var serverLnClient = new LnBotClient("uk_server", new LnBotClientOptions
        {
            HttpClient = new HttpClient(serverSdk.Handler) { BaseAddress = new Uri("https://api.ln.bot") },
        });

        // ── Client-side SDK: paying yields "L402 <macaroon>:pre" ──
        var payments = 0;
        var clientSdkHandler = new MockSdkHandler();
        clientSdkHandler.SetHandler("/l402/pay", request =>
        {
            payments++;
            var body = JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var macaroon = L402Headers.ParseChallenge(body.GetProperty("wwwAuthenticate").GetString())!.Value.Macaroon;
            return new
            {
                authorization = $"L402 {macaroon}:pre",
                paymentHash = macaroon["mac_".Length..],
                preimage = "pre",
                amount = 10,
                fee = 0,
                paymentNumber = payments,
                status = "settled",
            };
        });
        var clientLnClient = new LnBotClient("uk_client", new LnBotClientOptions
        {
            HttpClient = new HttpClient(clientSdkHandler) { BaseAddress = new Uri("https://api.ln.bot") },
        });

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(serverLnClient);
                    services.AddSingleton<ISpentTokenStore>(new MemorySpentTokenStore());
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseL402Paywall("/api/premium", new L402Options { WalletId = "wal_test", Price = 10, Description = "Premium API" });
                    app.UseL402Paywall("/api/pass", new L402Options { WalletId = "wal_test", Price = 10, Reuse = true, ExpirySeconds = 600 });
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/premium/data", () => Results.Ok(new { data = "premium content" }));
                        endpoints.MapGet("/api/pass/data", () => Results.Ok(new { data = "pass content" }));
                    });
                });
            })
            .Build();

        await host.StartAsync();

        var testServer = host.GetTestServer();
        var l402Handler = new L402DelegatingHandler(
            clientLnClient,
            new MemoryTokenStore(),
            new L402ClientOptions { WalletId = "wal_test", MaxPrice = 100 })
        {
            InnerHandler = testServer.CreateHandler(),
        };
        var client = new HttpClient(l402Handler) { BaseAddress = testServer.BaseAddress };
        return (host, client, () => payments);
    }

    /// <summary>Full roundtrip: 402, auto-pay, retry, 200. And the next request pays again.</summary>
    [Fact]
    public async Task FullRoundtrip_ClientPaysForEachRequest()
    {
        var (host, client, payments) = await StartAsync();

        var res = await client.GetAsync("/api/premium/data");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync());
        Assert.Equal("premium content", body.GetProperty("data").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/premium/data")).StatusCode);
        Assert.Equal(2, payments());

        client.Dispose();
        await host.StopAsync();
    }

    [Fact]
    public async Task SingleUseToken_DoesNotOpenThePass()
    {
        var (host, client, _) = await StartAsync();

        // Mint a single-use challenge, then present its token to the pass route.
        using var raw = host.GetTestClient();
        var challenge = await raw.GetAsync("/api/premium/data");
        var macaroon = L402Headers.ParseChallenge(challenge.Headers.WwwAuthenticate.ToString())!.Value.Macaroon;

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/pass/data");
        request.Headers.TryAddWithoutValidation("Authorization", $"L402 {macaroon}:pre");
        Assert.Equal((HttpStatusCode)402, (await raw.SendAsync(request)).StatusCode);

        client.Dispose();
        await host.StopAsync();
    }

    [Fact]
    public async Task Reuse_ClientPaysOnceForAPass()
    {
        var (host, client, payments) = await StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pass/data")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pass/data")).StatusCode);
        Assert.Equal(1, payments());

        client.Dispose();
        await host.StopAsync();
    }
}
