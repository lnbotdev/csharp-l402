namespace LnBot.L402;

/// <summary>
/// Remembers which tokens have been used, so a single-use token works once.
/// Register one in DI (or set <see cref="L402Options.SpentStore"/>) to share it between instances.
/// </summary>
public interface ISpentTokenStore
{
    /// <summary>
    /// Marks the token with this payment hash as used for <paramref name="ttl"/> (after which
    /// it has expired anyway). Returns true if this call claimed it, false if it was already
    /// used. Must be atomic across callers.
    /// </summary>
    Task<bool> ClaimAsync(string paymentHash, TimeSpan ttl, CancellationToken cancellationToken = default);
}
