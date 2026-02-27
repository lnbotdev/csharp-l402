using Microsoft.AspNetCore.Http;

namespace LnBot.L402;

/// <summary>
/// ASP.NET Core middleware that protects all routes under a path prefix with an L402 paywall.
/// </summary>
public class L402Middleware
{
    private readonly RequestDelegate _next;
    private readonly LnBotClient _client;
    private readonly L402Options _options;
    private readonly PathString _path;

    public L402Middleware(RequestDelegate next, LnBotClient client, L402Options options, PathString path)
    {
        _next = next;
        _client = client;
        _options = options;
        _path = path;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(_path))
        {
            await _next(context);
            return;
        }

        var price = _options.PriceFactory is not null
            ? await _options.PriceFactory(context)
            : _options.Price;

        if (await L402Handler.HandleAsync(_client, context, price, _options.Description))
        {
            await _next(context);
        }
    }
}
