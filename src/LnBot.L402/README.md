# LnBot.L402

[![NuGet](https://img.shields.io/nuget/v/LnBot.L402)](https://www.nuget.org/packages/LnBot.L402)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](https://github.com/lnbotdev/csharp-l402/blob/main/LICENSE)

**L402 Lightning payment client for .NET** — auto-pay any L402-protected API with `HttpClient`. Works in console apps, background services, MAUI — anything with `HttpClient`. Built on [ln.bot](https://ln.bot).

> Looking for server-side middleware? See [`LnBot.L402.AspNetCore`](https://www.nuget.org/packages/LnBot.L402.AspNetCore).

---

## What is L402?

[L402](https://github.com/lightninglabs/L402) is a protocol built on HTTP `402 Payment Required`. It enables machine-to-machine micropayments over the Lightning Network — ideal for API monetization, AI agent tool access, and pay-per-request data feeds.

---

## Install

```bash
dotnet add package LnBot.L402
```

---

## Quick Start

### With DI (ASP.NET Core, Worker Services)

```csharp
using LnBot;
using LnBot.L402;

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

### Console App (no ASP.NET Core)

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

## Options

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxPrice` | `int` | `int.MaxValue` | Max sats per request. Throws `L402BudgetExceededException` if exceeded. |
| `BudgetSats` | `long` | `0` (unlimited) | Rolling spend limit in sats. |
| `BudgetPeriod` | `BudgetPeriod` | `Day` | Reset interval: `Minute`, `Hour`, `Day`. |

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

services.AddSingleton<ITokenStore, RedisTokenStore>();
```

---

## Header Utilities

```csharp
using LnBot.L402;

L402Headers.ParseAuthorization("L402 mac_base64:preimage_hex");
L402Headers.ParseChallenge("L402 macaroon=\"abc\", invoice=\"lnbc1...\"");
L402Headers.FormatAuthorization("mac", "pre");
L402Headers.FormatChallenge("mac", "lnbc1...");
```

---

## How It Works

The `L402DelegatingHandler` intercepts `HttpClient` responses:

1. If the response is `402`, it reads the `WWW-Authenticate` header
2. Calls `client.L402.PayAsync()` to pay the Lightning invoice via the [ln.bot API](https://ln.bot/docs)
3. Retries the request with the `Authorization: L402 <macaroon>:<preimage>` header
4. Caches the token for future requests to the same URL

Zero crypto dependencies — all L402 logic lives in the [ln.bot API](https://ln.bot/docs).

---

## Requirements

- **.NET 8+**
- An [ln.bot](https://ln.bot) API key — [create a wallet](https://ln.bot/docs) to get one

## Related

- [`LnBot.L402.AspNetCore`](https://www.nuget.org/packages/LnBot.L402.AspNetCore) — Server-side middleware
- [`LnBot`](https://www.nuget.org/packages/LnBot) — The .NET SDK this package is built on
- [`@lnbot/l402`](https://www.npmjs.com/package/@lnbot/l402) — TypeScript equivalent

## Links

- [ln.bot](https://ln.bot) — website
- [Documentation](https://ln.bot/docs)
- [L402 specification](https://github.com/lightninglabs/L402)
- [GitHub](https://github.com/lnbotdev/csharp-l402)

## License

MIT
