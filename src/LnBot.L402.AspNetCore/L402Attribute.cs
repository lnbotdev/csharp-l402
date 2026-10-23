using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace LnBot.L402;

/// <summary>
/// Protects a controller action with an L402 paywall.
/// Requires LnBotClient to be registered in DI.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class L402Attribute : Attribute, IAsyncActionFilter
{
    public string WalletId { get; set; } = "";
    public int Price { get; set; }
    public string? Description { get; set; }

    /// <summary>Token lifetime in seconds; 0 means the default (3600 while tokens are single-use).</summary>
    public int ExpirySeconds { get; set; }

    public string[]? Caveats { get; set; }

    /// <summary>False (the default): one token, one request. True: a token works until it expires.</summary>
    public bool Reuse { get; set; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var client = context.HttpContext.RequestServices.GetRequiredService<LnBotClient>();

        if (await L402Handler.HandleAsync(client, WalletId, context.HttpContext, Price, Description,
            ExpirySeconds > 0 ? ExpirySeconds : null,
            Caveats?.Length > 0 ? new List<string>(Caveats) : null,
            Reuse))
        {
            await next();
        }
        else
        {
            // L402Handler already wrote the 402 response body.
            // Set an empty result to prevent MVC from overwriting it.
            context.Result = new EmptyResult();
        }
    }
}
