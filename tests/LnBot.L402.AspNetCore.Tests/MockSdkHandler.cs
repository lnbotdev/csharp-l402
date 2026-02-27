using System.Net;
using System.Text.Json;

namespace LnBot.L402.AspNetCore.Tests;

/// <summary>Mock handler that simulates the ln.bot SDK API for server-side tests.</summary>
internal class MockSdkHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, object>> _handlers = new();

    public void SetResponse(string pathContains, object responseBody) =>
        _handlers[pathContains] = _ => responseBody;

    public void SetHandler(string pathContains, Func<HttpRequestMessage, object> handler) =>
        _handlers[pathContains] = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        foreach (var (key, handler) in _handlers)
        {
            if (path.Contains(key))
            {
                var body = handler(request);
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
