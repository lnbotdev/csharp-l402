namespace LnBot.L402;

/// <summary>Pluggable L402 token cache.</summary>
public interface ITokenStore
{
    Task<L402Token?> GetAsync(string url);
    Task SetAsync(string url, L402Token token);
    Task DeleteAsync(string url);
}
