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

    public L402EndpointFilter(int price, string? description = null)
    {
        _price = price;
        _description = description;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var client = httpContext.RequestServices.GetRequiredService<LnBotClient>();

        if (await L402Handler.HandleAsync(client, httpContext, _price, _description))
        {
            return await next(context);
        }

        // L402Handler already wrote the 402 response — return empty to avoid double-write
        return Results.Empty;
    }
}
