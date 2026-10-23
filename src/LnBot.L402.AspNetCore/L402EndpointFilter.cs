using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LnBot.L402;

/// <summary>
/// Endpoint filter for minimal APIs that adds L402 paywall protection.
/// </summary>
public class L402EndpointFilter : IEndpointFilter
{
    private readonly string _walletId;
    private readonly int _price;
    private readonly string? _description;
    private readonly int? _expirySeconds;
    private readonly List<string>? _caveats;
    private readonly bool _reuse;

    /// <summary>A single-use paywall: one token, one request.</summary>
    public L402EndpointFilter(string walletId, int price, string? description = null, int? expirySeconds = null, List<string>? caveats = null)
        : this(walletId, price, description, expirySeconds, caveats, reuse: false)
    {
    }

    /// <param name="reuse">False: one token, one request. True: a token works until it expires.</param>
    public L402EndpointFilter(string walletId, int price, string? description, int? expirySeconds, List<string>? caveats, bool reuse)
    {
        L402Handler.ValidateCaveats(caveats, reuse);
        _walletId = walletId;
        _price = price;
        _description = description;
        _expirySeconds = expirySeconds;
        _caveats = caveats;
        _reuse = reuse;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var client = httpContext.RequestServices.GetRequiredService<LnBotClient>();

        if (await L402Handler.HandleAsync(client, _walletId, httpContext, _price, _description, _expirySeconds, _caveats, _reuse))
        {
            return await next(context);
        }

        // L402Handler already wrote the 402 response — return empty to avoid double-write
        return Results.Empty;
    }
}
