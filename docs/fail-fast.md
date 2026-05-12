# Fail Fast

Validate everything a system needs to operate — config, dependencies, connections — at startup, before accepting any traffic. If something is wrong, crash immediately with a clear error.

## Why it matters

A system that starts successfully but fails later is harder to diagnose and worse for users. The problem surfaces at runtime, under load, when it's hardest to fix. Fail Fast moves that failure to deploy time, where it belongs.

## What we validate

**Config is present and well-formed** — required values exist and pass format validation before the app starts serving requests.

**Services are wired up with valid parameters** — the HttpClient for Notifications uses the validated base URL, not a raw config lookup that bypasses validation.

## Implementation

Each service defines a typed options class with data annotation constraints:

```csharp
public sealed class NotificationsOptions
{
    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;
}
```

Registered in `Program.cs` with startup validation:

```csharp
builder.Services
    .AddOptions<NotificationsOptions>()
    .BindConfiguration("Notifications")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

The HttpClient resolves the validated options rather than reading from `IConfiguration` directly:

```csharp
builder.Services.AddHttpClient("Notifications", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<NotificationsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
```

## Gotchas

**`ValidateOnStart()` is not optional.** `ValidateDataAnnotations()` alone only validates when options are first accessed at runtime — that's Fail Eventually. `ValidateOnStart()` is what makes this actually run at startup.

**Don't read from `IConfiguration` inside `AddHttpClient`.** It bypasses the validated options pipeline. Always resolve `IOptions<T>` so validation is guaranteed to have run.

**Fail Fast covers config, not reachability.** It tells you the URL is present and well-formed. It does not tell you the downstream service is up. That's health checks and circuit breakers.

## What's deferred

- HttpClient timeout — fail fast at the request level, not just at startup
- Dependency reachability — startup health checks
- Input validation — reject malformed user input at the model/controller layer

These will be covered as separate patterns.