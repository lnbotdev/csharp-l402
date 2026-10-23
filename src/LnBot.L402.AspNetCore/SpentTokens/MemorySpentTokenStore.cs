using System.Collections.Concurrent;

namespace LnBot.L402;

/// <summary>
/// Default spent-token store: this process's memory. Each entry lives until its token has
/// expired. It forgets everything on restart and isn't shared between instances; register a
/// shared <see cref="ISpentTokenStore"/> when either matters.
/// </summary>
public sealed class MemorySpentTokenStore : ISpentTokenStore
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>The store paywalls use when none is configured.</summary>
    internal static MemorySpentTokenStore Shared { get; } = new();

    private readonly ConcurrentDictionary<string, DateTimeOffset> _spent = new();
    private readonly TimeProvider _time;
    private DateTimeOffset _lastSweep;

    public MemorySpentTokenStore() : this(TimeProvider.System) { }

    public MemorySpentTokenStore(TimeProvider time)
    {
        _time = time;
        _lastSweep = time.GetUtcNow();
    }

    public Task<bool> ClaimAsync(string paymentHash, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        Sweep(now);

        var until = now + ttl;
        while (true)
        {
            if (_spent.TryAdd(paymentHash, until))
                return Task.FromResult(true);
            if (_spent.TryGetValue(paymentHash, out var existing))
            {
                if (existing > now)
                    return Task.FromResult(false);
                // Remembered past its token's expiry: claim it afresh, unless another caller just did.
                if (_spent.TryUpdate(paymentHash, until, existing))
                    return Task.FromResult(true);
            }
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        if (now - _lastSweep < SweepInterval) return;
        _lastSweep = now;
        foreach (var (hash, until) in _spent)
        {
            if (until <= now)
                _spent.TryRemove(new KeyValuePair<string, DateTimeOffset>(hash, until));
        }
    }
}
