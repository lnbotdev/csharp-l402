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
builder.Services.AddSingleton(new LnBotClient("key_..."));

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
    PriceFactory = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api/dynamic/bulk"))
            return Task.FromResult(50);
        return Task.FromResult(5);
    }
});
```

---

## How It Works

The middleware makes two SDK calls:

- `client.L402.CreateChallengeAsync()` — creates an invoice + macaroon when a client needs to pay
- `client.L402.VerifyAsync()` — verifies an L402 authorization token when a client presents one

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
