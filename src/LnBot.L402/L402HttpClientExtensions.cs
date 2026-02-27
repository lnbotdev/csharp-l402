using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LnBot.L402;

public static class L402HttpClientExtensions
{
    /// <summary>
    /// Adds an L402 auto-pay handler to the HttpClient pipeline.
    /// Automatically pays 402 responses and retries with the L402 Authorization header.
    /// </summary>
    public static IHttpClientBuilder AddL402Handler(
        this IHttpClientBuilder builder,
        L402ClientOptions? options = null)
    {
        builder.Services.TryAddSingleton<ITokenStore, MemoryTokenStore>();
        return builder.AddHttpMessageHandler(sp =>
        {
            var client = sp.GetRequiredService<LnBotClient>();
            var store = sp.GetRequiredService<ITokenStore>();
            return new L402DelegatingHandler(client, store, options ?? new L402ClientOptions());
        });
    }
}
