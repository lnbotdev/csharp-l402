using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LnBot.L402;

/// <summary>
/// Endpoint filter for minimal APIs that adds L402 paywall protection.
/// </summary>
public class L402EndpointFilter : IEndpointFilter
{
    private readonly int _price;
    private readonly string? _description;
    private readonly int? _expirySeconds;
    private readonly List<string>? _caveats;

    public L402EndpointFilter(int price, string? description = null, int? expirySeconds = null, List<string>? caveats = null)
    {
        _price = price;
        _description = description;
        _expirySeconds = expirySeconds;
        _caveats = caveats;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var client = httpContext.RequestServices.GetRequiredService<LnBotClient>();

        if (await L402Handler.HandleAsync(client, httpContext, _price, _description, _expirySeconds, _caveats))
        {
            return await next(context);
        }

        // L402Handler already wrote the 402 response — return empty to avoid double-write
        return Results.Empty;
    }
}
