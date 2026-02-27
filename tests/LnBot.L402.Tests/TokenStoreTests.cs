using Xunit;

namespace LnBot.L402.Tests;

public class TokenStoreTests
{
    [Fact]
    public async Task MemoryStore_SetAndGet_ReturnsToken()
    {
        var store = new MemoryTokenStore();
        var token = new L402Token
        {
            Authorization = "L402 mac:pre",
            PaidAt = DateTimeOffset.UtcNow,
        };

        await store.SetAsync("https://example.com/api", token);
        var result = await store.GetAsync("https://example.com/api");

        Assert.NotNull(result);
        Assert.Equal("L402 mac:pre", result.Authorization);
    }

    [Fact]
    public async Task MemoryStore_GetMissing_ReturnsNull()
    {
        var store = new MemoryTokenStore();
        Assert.Null(await store.GetAsync("https://example.com/missing"));
    }

    [Fact]
    public async Task MemoryStore_Delete_RemovesToken()
    {
        var store = new MemoryTokenStore();
        await store.SetAsync("https://example.com/api", new L402Token
        {
            Authorization = "L402 mac:pre",
            PaidAt = DateTimeOffset.UtcNow,
        });

        await store.DeleteAsync("https://example.com/api");
        Assert.Null(await store.GetAsync("https://example.com/api"));
    }

    [Fact]
    public async Task MemoryStore_NormalizesUrls_StripsQueryAndTrailingSlash()
    {
        var store = new MemoryTokenStore();
        await store.SetAsync("https://example.com/api?key=value", new L402Token
        {
            Authorization = "L402 mac:pre",
            PaidAt = DateTimeOffset.UtcNow,
        });

        // Same path without query should match
        var result = await store.GetAsync("https://example.com/api/");
        Assert.NotNull(result);
    }

    [Fact]
    public void L402Token_IsExpired_ReturnsTrueWhenExpired()
    {
        var token = new L402Token
        {
            Authorization = "L402 mac:pre",
            PaidAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
        };

        Assert.True(token.IsExpired);
    }

    [Fact]
    public void L402Token_IsExpired_ReturnsFalseWhenNotExpired()
    {
        var token = new L402Token
        {
            Authorization = "L402 mac:pre",
            PaidAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };

        Assert.False(token.IsExpired);
    }

    [Fact]
    public void L402Token_IsExpired_ReturnsFalseWhenNoExpiry()
    {
        var token = new L402Token
        {
            Authorization = "L402 mac:pre",
            PaidAt = DateTimeOffset.UtcNow,
        };

        Assert.False(token.IsExpired);
    }
}
