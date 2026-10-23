# LnBot.L402.AspNetCore

[![NuGet](https://img.shields.io/nuget/v/LnBot.L402.AspNetCore)](https://www.nuget.org/packages/LnBot.L402.AspNetCore)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](https://github.com/lnbotdev/csharp-l402/blob/main/LICENSE)

**L402 Lightning payment middleware for ASP.NET Core** — paywall any API in one line. Built on [ln.bot](https://ln.bot).

Protect ASP.NET Core routes behind L402 paywalls with middleware, `[L402]` attributes, or endpoint filters. Includes the [`LnBot.L402`](https://www.nuget.org/packages/LnBot.L402) client package.

---

## What is L402?

[L402](https://github.com/lightninglabs/L402) is a protocol built on HTTP `402 Payment Required`. It enables machine-to-machine micropayments over the Lightning Network — ideal for API monetization, AI agent tool access, and pay-per-request data feeds.

---

## Install

```bash
dotnet add package LnBot.L402.AspNetCore
```

This includes the client package (`LnBot.L402`) — no need to install both.

---

## Quick Start

### Middleware pipeline

```csharp
using LnBot;
using LnBot.L402;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new LnBotClient("uk_..."));

var app = builder.Build();

app.UseL402Paywall("/api/premium", new L402Options
{
    Price = 10,
    Description = "API access",
});

app.MapGet("/api/premium/data", () => Results.Ok(new { data = "premium content" }));
app.MapGet("/api/free/health", () => Results.Ok(new { status = "ok" }));

app.Run();
```

### Controller attribute

```csharp
using LnBot.L402;

[ApiController]
[Route("api/[controller]")]
public class WeatherController : ControllerBase
{
    [L402(Price = 50, Description = "Weather forecast")]
    [HttpGet("forecast")]
    public IActionResult GetForecast()
        => Ok(new { forecast = "sunny" });
}
```

### Minimal API endpoint filter

```csharp
app.MapGet("/api/premium/data", () => Results.Ok(new { data = "premium" }))
   .AddEndpointFilter(new L402EndpointFilter(price: 10, description: "API access"));
```

### Dynamic pricing

```csharp
app.UseL402Paywall("/api/dynamic", new L402Options
{
    WalletId = "wal_...",
    PriceFactory = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api/dynamic/bulk"))
            return Task.FromResult(50);
        return Task.FromResult(5);
    }
});
```

### One token, one request

By default each payment buys one request. Every challenge carries two caveats, `lnbot_path=<path>` and `lnbot_price=<sats>`, so the token it produces only opens the route it was issued for, at that price or less. A holder can add caveats to a macaroon but never remove them, so a token can't be widened. Tokens expire after an hour unless you set `ExpirySeconds`, and a spent token is remembered until then.

Spent tokens are remembered in memory by default. If your API runs on more than one instance, register one `ISpentTokenStore` they all share, or a token spent on one instance could be spent again on another:

```csharp
using StackExchange.Redis;

public sealed class RedisSpentTokenStore(IConnectionMultiplexer redis) : ISpentTokenStore
{
    // SET NX with an expiry: true only for the first caller
    public Task<bool> ClaimAsync(string paymentHash, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        redis.GetDatabase().StringSetAsync($"l402:spent:{paymentHash}", "1", ttl, When.NotExists);
}

builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("localhost"));
builder.Services.AddSingleton<ISpentTokenStore, RedisSpentTokenStore>();
```

A token is spent when the request reaches your endpoint, even if the endpoint then fails.

### Passes: `Reuse = true`

For "pay once, use for an hour", let tokens be reused until they expire. This is how 1.0 behaved, with no route or price binding:

```csharp
app.UseL402Paywall("/api/pass", new L402Options
{
    WalletId = "wal_...",
    Price = 500,
    Reuse = true,
    ExpirySeconds = 3600, // without it, a reusable token never expires
});
```

The `[L402]` attribute takes `Reuse = true` too, and `L402EndpointFilter` takes `reuse: true`.

---

## How It Works

The middleware makes two SDK calls:

- `client.Wallet(walletId).L402.CreateChallengeAsync()` — creates an invoice + macaroon when a client needs to pay
- `client.Wallet(walletId).L402.VerifyAsync()` — verifies an L402 authorization token when a client presents one

Then it checks that the token was issued for this route and price, and marks it spent (unless `Reuse` is set).

All L402 logic — macaroon creation, signature verification, preimage checking — lives in the [ln.bot API](https://ln.bot/docs). Zero crypto dependencies.

---

## Requirements

- **.NET 8+**
- An [ln.bot](https://ln.bot) API key — [create a wallet](https://ln.bot/docs) to get one

## Related

- [`LnBot.L402`](https://www.nuget.org/packages/LnBot.L402) — Client-side auto-pay handler
- [`LnBot`](https://www.nuget.org/packages/LnBot) — The .NET SDK this package is built on
- [`@lnbot/l402`](https://www.npmjs.com/package/@lnbot/l402) — TypeScript equivalent

## Links

- [ln.bot](https://ln.bot) — website
- [Documentation](https://ln.bot/docs)
- [L402 specification](https://github.com/lightninglabs/L402)
- [GitHub](https://github.com/lnbotdev/csharp-l402)

## License

MIT
