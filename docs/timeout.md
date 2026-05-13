# Timeout

## What it is

A timeout bounds how long a caller will wait for a downstream operation before giving up. Without one, a slow or hung dependency holds threads open indefinitely. Enough of those and the caller stops serving anything — a cascading failure caused by someone else's problem.

## Where it belongs

At every integration point — any place you cross a process, network, or I/O boundary. In this solution, that means every outbound `HttpClient` call from `Todos.Web` to `Todos.Notifications`.

## Implementation

Timeout is configured on the named `HttpClient` via `NotificationsOptions`, so it can be adjusted per environment without a redeploy.

**`NotificationsOptions.cs`**

```csharp
[Range(1, 30)]
public int TimeoutSeconds { get; set; } = 5;
```

`[Range(1, 30)]` combined with `ValidateDataAnnotations()` on the options registration means a bad value fails at startup, not silently at runtime.

**`Program.cs`**

```csharp
builder.Services.AddHttpClient("Notifications", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<NotificationsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
```

**Controller action**

```csharp
public async Task<IActionResult> Index(CancellationToken cancellationToken)
{
    HttpClient client = _httpClientFactory.CreateClient("Notifications");

    try
    {
        HttpResponseMessage response = await client.GetAsync("/notifications", cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        return Content(content, "application/json");
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Problem(
            detail: "The Notifications service did not respond in time.",
            title: "Gateway Timeout",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
}
```

## CancellationToken

ASP.NET Core provides a `CancellationToken` on every action, tied to the request lifetime. It fires if the user aborts. Passing it into the `HttpClient` call means an abandoned request doesn't keep running pointlessly downstream.

Two things can now cancel the request — the timeout and the user abort — and both surface as `TaskCanceledException`. The `when (!cancellationToken.IsCancellationRequested)` guard tells them apart: if the user's token fired, let the exception propagate naturally; if it didn't, the timeout fired and we return 504.

## Gotchas

- `HttpClient.Timeout` throws `TaskCanceledException`, not `TimeoutException`. Always use the `when` guard.
- The timeout is client-side only. The server keeps processing after you give up.
- Too tight → false failures under normal load spikes. Too loose → doesn't protect you. Base it on your p99 latency, not a guess.

## What's next

Timeout bounds the wait. It doesn't handle repeated failures (Retry) or stop the caller from hammering a struggling service (Circuit Breaker). The `catch` block in the controller is also temporary — once those patterns are in place, error handling moves to a delegating handler and controllers stay clean.