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

public class MiddlewareTests
{
    private static (IHost Host, MockSdkHandler SdkHandler) CreateTestHost()
    {
        var sdkHandler = new MockSdkHandler();
        var sdkHttpClient = new HttpClient(sdkHandler) { BaseAddress = new Uri("https://api.ln.bot") };
        var lnClient = new LnBotClient("key_test", new LnBotClientOptions { HttpClient = sdkHttpClient });

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton(lnClient);
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseL402Paywall("/api/premium", new L402Options
                    {
                        Price = 10,
                        Description = "Test API",
                    });
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/premium/data", () => Results.Ok(new { data = "premium" }));
                        endpoints.MapGet("/api/free/health", () => Results.Ok(new { status = "ok" }));
                    });
                });
            })
            .Build();

        return (host, sdkHandler);
    }

    [Fact]
    public async Task FreeRoute_PassesThrough()
    {
        var (host, _) = CreateTestHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var res = await client.GetAsync("/api/free/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("ok", body);

        await host.StopAsync();
    }

    [Fact]
    public async Task ProtectedRoute_NoAuth_Returns402()
    {
        var (host, sdkHandler) = CreateTestHost();

        sdkHandler.SetResponse("/v1/l402/challenges", new
        {
            macaroon = "test_mac",
            invoice = "lnbc10n1test",
            paymentHash = "hash",
            expiresAt = "2099-01-01T00:00:00Z",
            wwwAuthenticate = "L402 macaroon=\"test_mac\", invoice=\"lnbc10n1test\"",
        });

        await host.StartAsync();
        var client = host.GetTestClient();

        var res = await client.GetAsync("/api/premium/data");

        Assert.Equal((HttpStatusCode)402, res.StatusCode);
        Assert.Contains("L402", res.Headers.WwwAuthenticate.ToString());

        var body = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync());
        Assert.Equal("payment_required", body.GetProperty("type").GetString());
        Assert.Equal(10, body.GetProperty("price").GetInt32());

        await host.StopAsync();
    }

    [Fact]
    public async Task ProtectedRoute_ValidAuth_Returns200()
    {
        var (host, sdkHandler) = CreateTestHost();

        sdkHandler.SetResponse("/v1/l402/verify", new
        {
            valid = true,
            paymentHash = "hash123",
            caveats = (List<string>?)null,
            error = (string?)null,
        });

        await host.StartAsync();
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/premium/data");
        request.Headers.TryAddWithoutValidation("Authorization", "L402 mac:preimage");

        var res = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("premium", body);

        await host.StopAsync();
    }

    [Fact]
    public async Task ProtectedRoute_InvalidAuth_Returns402Challenge()
    {
        var (host, sdkHandler) = CreateTestHost();

        sdkHandler.SetResponse("/v1/l402/verify", new
        {
            valid = false,
            paymentHash = (string?)null,
            caveats = (List<string>?)null,
            error = "invalid preimage",
        });
        sdkHandler.SetResponse("/v1/l402/challenges", new
        {
            macaroon = "new_mac",
            invoice = "lnbc_new",
            paymentHash = "hash",
            expiresAt = "2099-01-01T00:00:00Z",
            wwwAuthenticate = "L402 macaroon=\"new_mac\", invoice=\"lnbc_new\"",
        });

        await host.StartAsync();
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/premium/data");
        request.Headers.TryAddWithoutValidation("Authorization", "L402 bad:bad");

        var res = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)402, res.StatusCode);

        await host.StopAsync();
    }
}
