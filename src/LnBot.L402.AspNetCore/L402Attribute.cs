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
    public int Price { get; set; }
    public string? Description { get; set; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var client = context.HttpContext.RequestServices.GetRequiredService<LnBotClient>();

        if (await L402Handler.HandleAsync(client, context.HttpContext, Price, Description))
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
