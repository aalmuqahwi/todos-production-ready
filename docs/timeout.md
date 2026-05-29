# Timeout

## What it is

A timeout is an acknowledgment that you can't trust other systems to respond. Not because they're bad — because they're separate. A network can stall, a service can hang, a thread pool can exhaust. You were never guaranteed a response, you just assumed one.

A timeout makes that assumption explicit. You decide, upfront, how long a dependency is worth waiting for. When that time passes, you stop waiting and deal with the situation yourself — rather than letting someone else's problem become yours.

## Where it belongs

Any place you hand control to something outside your process — an HTTP call, a database query, a message queue, a file read over a network share — is a place you need a timeout. You're crossing a boundary you don't control. Without a timeout, a slow or hung dependency holds your threads open. Enough of those and you stop serving anything, not because you're broken, but because you're stuck waiting for something that isn't coming.

In this solution, the boundary is the HTTP call from `Todos.Web` to `Todos.Notifications`.

## How it works in .NET
`HttpClient.Timeout` is the simplest way to express this. Set it once on the named client and it applies to every request that client makes. When the deadline passes, .NET cancels the request internally — which is why it throws `TaskCanceledException` and not `TimeoutException`. The timeout is implemented as a cancellation under the hood.

ASP.NET Core also hands you a `CancellationToken` on every action, tied to the HTTP request lifetime. This one fires when the user aborts — navigates away, closes the tab, kills the connection. Pass it into your `HttpClient` call and the two tokens compose naturally: whichever fires first cancels the request. You get both deadline enforcement and abort propagation for free.

## Implementation

Timeout is driven by `NotificationsOptions` so it can be tuned per environment without a redeploy.

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
public async Task<IActionResult> Create(string title, CancellationToken cancellationToken)
{
    HttpClient client = _httpClientFactory.CreateClient("Notifications");

    await client.PostAsJsonAsync(
        "/notifications",
        new { TodoId = _nextId, Message = $"Todo '{title}' was created." },
        cancellationToken);

    Todo todo = new(_nextId++, title);
    _todos.Add(todo);

    return RedirectToAction(nameof(Index));
}
```

The controller has no `try/catch`. Error handling — including timeout detection — lives in `NotificationsResilienceHandler`. The controller calls `PostAsJsonAsync` and moves on; any exception propagates to the global handler.

## CancellationToken

Both a timeout and a user abort surface as `TaskCanceledException`. Without a `when` guard you can't tell them apart — and it matters, because they mean different things. A timeout means the downstream service is slow or hung. A user abort means the client gave up and the response would go nowhere anyway.

In `NotificationsResilienceHandler`, the guard reads:

```csharp
catch (TaskCanceledException) when (!(_httpContextAccessor.HttpContext?.RequestAborted.IsCancellationRequested ?? false))
    => throw new NotificationsTimeoutException();
```

If the request `CancellationToken` hasn't fired, the user didn't abort — so the timeout fired. Throw `NotificationsTimeoutException`. If the request token did fire, let the `TaskCanceledException` propagate. ASP.NET Core handles it cleanly — the connection is already gone.

## Gotchas

- `HttpClient.Timeout` throws `TaskCanceledException`, not `TimeoutException`. The `when` guard is not optional.
- The timeout is yours, not theirs. The server keeps processing after you give up. If the downstream service charges per request, you're still charged.
- Too tight and you get false failures under normal load spikes. Too loose and you're not actually protected. Base it on your p99 latency, not a guess.

## Scope of this implementation

Timeout isn't an HTTP concept — it's a boundary concept. It applies anywhere you wait for something outside your control: database queries, message queues, file I/O, any external call. This implementation covers `HttpClient` only because that's the only integration point in the app right now. As the app grows, the same thinking applies at every new boundary.

## What's next

Timeout bounds the wait. It doesn't handle repeated failures (Retry) or stop the caller from hammering a struggling service (Circuit Breaker). Those patterns are covered next and share the same resilience pipeline.