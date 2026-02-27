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
    /// <summary>
    /// Full roundtrip: L402 client hits protected server → gets 402 → auto-pays → retries → gets 200.
    /// </summary>
    [Fact]
    public async Task FullRoundtrip_ClientPaysAndAccessesProtectedRoute()
    {
        // ── Server-side SDK mock (createChallenge + verify) ──
        var serverSdkHandler = new MockSdkHandler();
        var serverSdkHttp = new HttpClient(serverSdkHandler) { BaseAddress = new Uri("https://api.ln.bot") };
        var serverLnClient = new LnBotClient("key_server", new LnBotClientOptions { HttpClient = serverSdkHttp });

        serverSdkHandler.SetResponse("/v1/l402/challenges", new
        {
            macaroon = "test_mac",
            invoice = "lnbc10n1test",
            paymentHash = "test_hash",
            expiresAt = "2099-01-01T00:00:00Z",
            wwwAuthenticate = "L402 macaroon=\"test_mac\", invoice=\"lnbc10n1test\"",
        });
        serverSdkHandler.SetResponse("/v1/l402/verify", new
        {
            valid = true,
            paymentHash = "test_hash",
            caveats = (List<string>?)null,
            error = (string?)null,
        });

        // ── Client-side SDK mock (pay) ──
        var clientSdkHandler = new MockSdkHandler();
        var clientSdkHttp = new HttpClient(clientSdkHandler) { BaseAddress = new Uri("https://api.ln.bot") };
        var clientLnClient = new LnBotClient("key_client", new LnBotClientOptions { HttpClient = clientSdkHttp });

        clientSdkHandler.SetResponse("/v1/l402/pay", new
        {
            authorization = "L402 test_mac:test_preimage",
            paymentHash = "test_hash",
            preimage = "test_preimage",
            amount = 10,
            fee = 0,
            paymentNumber = 1,
            status = "settled",
        });

        // ── Server ──
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(serverLnClient);
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseL402Paywall("/api/premium", new L402Options
                    {
                        Price = 10,
                        Description = "Premium API",
                    });
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/premium/data", () =>
                            Results.Ok(new { data = "premium content" }));
                    });
                });
            })
            .Build();

        await host.StartAsync();

        // ── Client with L402 auto-pay ──
        var testServer = host.GetTestServer();
        var l402Handler = new L402DelegatingHandler(
            clientLnClient,
            new MemoryTokenStore(),
            new L402ClientOptions { MaxPrice = 100 })
        {
            InnerHandler = testServer.CreateHandler(),
        };
        using var client = new HttpClient(l402Handler) { BaseAddress = testServer.BaseAddress };

        // Step 1: First request — should auto-pay the 402 and get 200
        var res = await client.GetAsync("/api/premium/data");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync());
        Assert.Equal("premium content", body.GetProperty("data").GetString());

        await host.StopAsync();
    }
}
