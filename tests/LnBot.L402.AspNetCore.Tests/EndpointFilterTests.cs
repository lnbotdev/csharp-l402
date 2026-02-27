using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LnBot.L402.AspNetCore.Tests;

public class EndpointFilterTests
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
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/filtered", () => Results.Ok(new { data = "filtered" }))
                            .AddEndpointFilter(new L402EndpointFilter(25, "Filtered endpoint"));
                    });
                });
            })
            .Build();

        return (host, sdkHandler);
    }

    [Fact]
    public async Task NoAuth_Returns402()
    {
        var (host, sdkHandler) = CreateTestHost();

        sdkHandler.SetResponse("/v1/l402/challenges", new
        {
            macaroon = "mac",
            invoice = "inv",
            paymentHash = "hash",
            expiresAt = "2099-01-01T00:00:00Z",
            wwwAuthenticate = "L402 macaroon=\"mac\", invoice=\"inv\"",
        });

        await host.StartAsync();
        var client = host.GetTestClient();

        var res = await client.GetAsync("/api/filtered");
        Assert.Equal((HttpStatusCode)402, res.StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task ValidAuth_Returns200()
    {
        var (host, sdkHandler) = CreateTestHost();

        sdkHandler.SetResponse("/v1/l402/verify", new
        {
            valid = true,
            paymentHash = "hash",
            caveats = (List<string>?)null,
            error = (string?)null,
        });

        await host.StartAsync();
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/filtered");
        request.Headers.TryAddWithoutValidation("Authorization", "L402 mac:pre");

        var res = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("filtered", body);

        await host.StopAsync();
    }
}
