namespace LnBot.L402;

/// <summary>A cached L402 credential.</summary>
public class L402Token
{
    /// <summary>The full Authorization header value, e.g. "L402 mac:preimage".</summary>
    public required string Authorization { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow >= ExpiresAt.Value;
}
