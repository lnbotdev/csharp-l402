# LnBot.L402

[![NuGet](https://img.shields.io/nuget/v/LnBot.L402)](https://www.nuget.org/packages/LnBot.L402)
[![NuGet](https://img.shields.io/nuget/v/LnBot.L402.AspNetCore)](https://www.nuget.org/packages/LnBot.L402.AspNetCore)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](./LICENSE)

**L402 Lightning payment middleware for .NET** — paywall any API in one line. Built on [ln.bot](https://ln.bot).

Two NuGet packages:

- **`LnBot.L402`** — Client-side. Auto-pay L402-protected APIs with any `HttpClient`. Works in console apps, background services, MAUI — anything with `HttpClient`.
- **`LnBot.L402.AspNetCore`** — Server-side. Protect ASP.NET Core routes behind L402 paywalls with middleware, `[L402]` attributes, or endpoint filters.

Both packages are thin glue layers. All L402 logic — macaroon creation, signature verification, preimage checking — lives in the [ln.bot API](https://ln.bot/docs) via the [`LnBot` SDK](https://www.nuget.org/packages/LnBot). Zero crypto dependencies.

---

## What is L402?

[L402](https://github.com/lightninglabs/L402) is a protocol built on HTTP `402 Payment Required`. It enables machine-to-machine micropayments over the Lightning Network:

1. **Client** requests a protected resource
2. **Server** returns `402` with a Lightning invoice and a macaroon token
3. **Client** pays the invoice, obtains the preimage as proof of payment
4. **Client** retries the request with `Authorization: L402 <macaroon>:<preimage>`
5. **Server** verifies the token and grants access

L402 is ideal for API monetization, AI agent tool access, pay-per-request data feeds, and any scenario where you want instant, permissionless, per-request payments without subscriptions or API key provisioning.

---

## Install

```bash
dotnet add package LnBot.L402.AspNetCore   # Server (includes client package)
dotnet add package LnBot.L402              # Client only (no ASP.NET Core dependency)
```

---

## Server — Protect Routes with L402

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

## Client — Auto-Pay L402 APIs

### With ASP.NET Core DI

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new LnBotClient("key_..."));

builder.Services.AddHttpClient("paid-apis")
    .AddL402Handler(new L402ClientOptions
    {
        MaxPrice = 100,
        BudgetSats = 50_000,
        BudgetPeriod = BudgetPeriod.Day,
    });

var app = builder.Build();

app.MapGet("/proxy", async (IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("paid-apis");
    var data = await http.GetStringAsync("https://api.example.com/premium/data");
    return Results.Ok(data);
});
```

### Console app (no ASP.NET Core)

```csharp
using LnBot;
using LnBot.L402;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton(new LnBotClient("key_..."));
services.AddSingleton<ITokenStore, MemoryTokenStore>();
services.AddHttpClient("paid-apis").AddL402Handler();

var provider = services.BuildServiceProvider();
var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("paid-apis");

// Auto-pays any 402 responses transparently
var response = await http.GetStringAsync("https://api.example.com/premium/data");
Console.WriteLine(response);
```

---

## Header Utilities

```csharp
using LnBot.L402;

// Parse Authorization: L402 <macaroon>:<preimage>
var auth = L402Headers.ParseAuthorization("L402 mac_base64:preimage_hex");
// → (Macaroon: "mac_base64", Preimage: "preimage_hex")

// Parse WWW-Authenticate: L402 macaroon="...", invoice="..."
var challenge = L402Headers.ParseChallenge("L402 macaroon=\"abc\", invoice=\"lnbc1...\"");
// → (Macaroon: "abc", Invoice: "lnbc1...")

// Format headers
L402Headers.FormatAuthorization("mac", "pre");     // → "L402 mac:pre"
L402Headers.FormatChallenge("mac", "lnbc1...");    // → "L402 macaroon=\"mac\", invoice=\"lnbc1...\""
```

---

## Custom Token Store

Implement `ITokenStore` for Redis, file system, or any persistence layer:

```csharp
public class RedisTokenStore : ITokenStore
{
    public Task<L402Token?> GetAsync(string url) { /* ... */ }
    public Task SetAsync(string url, L402Token token) { /* ... */ }
    public Task DeleteAsync(string url) { /* ... */ }
}

// Register in DI
services.AddSingleton<ITokenStore, RedisTokenStore>();
```

---

## How It Works

**Server middleware** makes two SDK calls:
- `client.L402.CreateChallengeAsync()` — creates an invoice + macaroon when a client needs to pay
- `client.L402.VerifyAsync()` — verifies an L402 authorization token when a client presents one

**Client handler** makes one SDK call:
- `client.L402.PayAsync()` — pays a Lightning invoice and returns a ready-to-use Authorization header

---

## Requirements

- **.NET 8+**
- An [ln.bot](https://ln.bot) API key — [create a wallet](https://ln.bot/docs) to get one

## Related packages

- [`LnBot`](https://www.nuget.org/packages/LnBot) — The .NET SDK this package is built on
- [`@lnbot/l402`](https://www.npmjs.com/package/@lnbot/l402) — TypeScript/Express.js equivalent
- [`@lnbot/sdk`](https://www.npmjs.com/package/@lnbot/sdk) — TypeScript SDK

## Links

- [ln.bot](https://ln.bot) — website
- [Documentation](https://ln.bot/docs)
- [L402 specification](https://github.com/lightninglabs/L402)
- [GitHub](https://github.com/lnbotdev)

## License

MIT
