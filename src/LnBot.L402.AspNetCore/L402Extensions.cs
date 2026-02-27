using LnBot.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LnBot.L402;

public static class L402Extensions
{
    /// <summary>
    /// Adds L402 paywall middleware that protects all routes under the given path prefix.
    /// Requires LnBotClient to be registered in DI.
    /// </summary>
    public static IApplicationBuilder UseL402Paywall(
        this IApplicationBuilder app,
        string path,
        L402Options options)
    {
        var client = app.ApplicationServices.GetRequiredService<LnBotClient>();
        return app.UseMiddleware<L402Middleware>(client, options, new PathString(path));
    }

    /// <summary>
    /// Gets the L402 verification result from <c>HttpContext.Items</c> after a successful paywall check.
    /// Returns null if no L402 verification has been performed.
    /// </summary>
    public static VerifyL402Response? GetL402(this HttpContext context)
        => context.Items["L402"] as VerifyL402Response;
}
