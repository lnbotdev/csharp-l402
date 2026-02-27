using System.Collections.Concurrent;

namespace LnBot.L402;

/// <summary>Default in-memory token cache backed by ConcurrentDictionary.</summary>
public class MemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, L402Token> _tokens = new();

    public Task<L402Token?> GetAsync(string url)
    {
        _tokens.TryGetValue(NormalizeUrl(url), out var token);
        return Task.FromResult(token);
    }

    public Task SetAsync(string url, L402Token token)
    {
        _tokens[NormalizeUrl(url)] = token;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string url)
    {
        _tokens.TryRemove(NormalizeUrl(url), out _);
        return Task.CompletedTask;
    }

    private static string NormalizeUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}".TrimEnd('/');
        return url;
    }
}
