# Create Back Pressure

## What it is

Create Back Pressure is the pattern of signalling capacity limits upstream so that callers slow down cooperatively, rather than being rejected silently. Where Shed Load drops requests at the boundary and returns a 503, Back Pressure adds a `Retry-After` header to that 503 — a specific, machine-readable instruction: "I am busy; try again in N seconds." A caller that honours it waits the indicated time before retrying instead of immediately hammering the service again.

## Why it matters

A plain 503 tells the caller something failed. It does not tell the caller how long to wait. Without that signal, two bad outcomes compete:

- **Too fast:** the retry fires immediately (or with arbitrary exponential backoff that may be too aggressive), adding load to a service that is already at capacity — the opposite of the intended effect.
- **Too slow:** the retry uses a conservatively long delay that was not tuned to this service, so throughput recovers more slowly than it needs to.

`Retry-After` gives the service control over the pacing of recovery. The service knows its window — one second for a fixed-window limiter — and can tell callers exactly when capacity will free up. The caller does not have to guess.

This is the cooperative half of the shed-load feedback loop. Shed Load is reactive: it rejects work that has already arrived. Back Pressure is proactive: it shapes the arrival rate so that future requests are timed to land when capacity exists.

## Where it belongs in this app

`Todos.Notifications` sets the `Retry-After` header in the rate limiter's rejection handler. `Todos.Web` reads that header in the retry policy's `DelayGenerator` and waits the indicated duration before the next attempt.

Neither side changes the other's core configuration. The rate limiter policy and the retry policy remain unchanged — Back Pressure is a single hook on each side.

## Implementation

### `Todos.Notifications` — set the header on rejection

```csharp
options.OnRejected = async (context, cancellationToken) =>
{
    context.HttpContext.Response.Headers.RetryAfter = "1";
    context.HttpContext.Response.ContentType = "text/plain";
    await context.HttpContext.Response.WriteAsync("Service busy. Retry after 1 second.", cancellationToken);
};
```

`OnRejected` fires after `RejectionStatusCode` has been set, so the 503 status is already written. The handler adds the `Retry-After` header and a human-readable body. The value `"1"` matches the rate limiter's `Window` of one second — the exact point at which the next window opens.

### `Todos.Web` — honour the header in the retry delay

```csharp
DelayGenerator = static args =>
{
    if (args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.ServiceUnavailable } response &&
        response.Headers.TryGetValues("Retry-After", out var values) &&
        int.TryParse(values.FirstOrDefault(), out var seconds))
    {
        return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromSeconds(seconds));
    }

    return ValueTask.FromResult<TimeSpan?>(null);
}
```

`DelayGenerator` is called by Polly before each retry delay. Returning a non-null `TimeSpan` overrides the configured `BackoffType` and `UseJitter` for that attempt. Returning `null` falls back to the normal exponential backoff — so this only changes behaviour when a 503 with a parseable `Retry-After` header is present. All other failures (network errors, 500s, non-standard 503s) continue using exponential backoff with jitter.

The predicate checks three conditions in sequence:
1. The response status is 503 (not a network exception, not another 5xx)
2. A `Retry-After` header is present
3. The header value is a valid integer

If any condition fails, the function returns `null`. This keeps the happy path explicit and avoids swallowing parse errors silently.

## What the UI shows

Back Pressure does not change what a user sees during normal operation. The effect is in timing: retries now wait one second rather than the jittered exponential delay that might have fired sooner or later. Under sustained shedding, this lowers the retry rate, which reduces the failure ratio, which slows the circuit breaker's progress toward opening.

In practice, light bursts will be absorbed by the retry-with-backpressure combination and the user will see nothing. Heavy, sustained overload still trips the circuit breaker and surfaces the unavailability error page — Back Pressure is not a substitute for Shed Load, it is a refinement of the recovery signal.

## Gotchas

- **The header value must match the rate limiter window.** If the window is 1 second and `Retry-After` says 5, callers back off longer than necessary and throughput recovers slowly. If it says 0, callers retry immediately and the header does nothing. Keep the two values in sync.
- **`DelayGenerator` overrides jitter for the affected attempt.** Returning a fixed `TimeSpan.FromSeconds(1)` means all instances of `Todos.Web` will retry at exactly the same moment after a shed. For most window sizes this is acceptable — the window resets for all of them simultaneously — but if thundering-herd concerns apply, add a small random offset to the returned value.
- **This only controls the delay, not whether to retry.** `ShouldHandle` still determines whether a 503 is retried at all. The two properties are independent: `ShouldHandle` decides if, `DelayGenerator` decides when.
- **Non-integer `Retry-After` values are ignored.** The HTTP specification allows date strings (e.g., `Retry-After: Thu, 01 Jan 2026 00:00:00 GMT`). The current implementation only handles integer seconds — parsing a date is not needed here and the predicate falls back to exponential backoff gracefully.

## What's next

Back Pressure shapes retry pacing for a single caller-callee pair. The Governor pattern extends this concept to autonomous rate management: a service monitors its own resource consumption over time and throttles its own output when it detects sustained pressure, independent of any single caller's behaviour.
