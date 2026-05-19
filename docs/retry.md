# Retry

## What it is

A retry policy re-issues a failed request automatically, up to a configured number of attempts, before propagating the failure to the caller. The goal is to absorb transient errors — brief network blips, momentary service restarts, ephemeral 503s — without the caller ever knowing they happened.

## Why it matters

Not all failures are equal. Some reflect a genuine, durable problem (the service is down, the request is malformed). Others are noise: a TCP reset during a rolling deploy, a momentary connection pool exhaustion, a 502 from a load balancer that was mid-restart. Without retry, those transient errors surface as user-visible failures even though a second attempt a moment later would have succeeded.

Retry converts a class of false failures into successes. The tradeoff is added latency on the unhappy path and, if misconfigured, extra load on a struggling dependency.

## Where it belongs in this app

The boundary is the HTTP call from `Todos.Web` to `Todos.Notifications`. Retry belongs at that boundary, in the `HttpClient` resilience pipeline. It is invisible to the controller. The controller calls `PostAsJsonAsync`. If the first attempt fails with a transient error, the resilience layer retries silently. Only after all attempts are exhausted does an exception propagate.

## What to retry on — and what not to

**Retry:**
- `HttpRequestException` — covers TCP-level failures: refused connections, DNS failures, connection resets.
- 5xx responses — the server acknowledged the request but failed to handle it. A 503 during a restart is worth retrying; a persistent 500 will exhaust retries and then trip the circuit breaker.

**Do not retry:**
- 4xx responses — the problem is with the request itself, not the server. Retrying a 400, 401, or 404 will never succeed and wastes attempts. A 422 is a domain validation failure. None of these are transient.

The `ShouldHandle` predicate in `Program.cs` encodes this rule explicitly:

```csharp
ShouldHandle = static args => ValueTask.FromResult(
    args.Outcome.Exception is HttpRequestException ||
    (args.Outcome.Result is { IsSuccessStatusCode: false } r && (int)r.StatusCode >= 500))
```

## Backoff and jitter

Retrying immediately after a failure is dangerous. If a service is overloaded or restarting, hitting it again a millisecond later adds to its load without giving it time to recover.

**Exponential backoff** spaces retries out: the delay doubles with each attempt (e.g., ~1s, ~2s, ~4s). This gives the dependency breathing room.

**Jitter** adds a random offset to each delay. Without it, all instances of your service retry at exactly the same moment — a thundering herd that can overwhelm the dependency just as it tries to recover. Jitter spreads the retries out in time across instances.

Both are configured via `BackoffType = DelayBackoffType.Exponential` and `UseJitter = true`.

## Pipeline order — Retry → Circuit Breaker

The pipeline is configured in this order:

```csharp
resilienceBuilder.AddRetry(...);       // outermost
resilienceBuilder.AddCircuitBreaker(...); // innermost
```

Polly executes strategies outermost-first. A request enters Retry, then Circuit Breaker, then the network. On failure, the Circuit Breaker counts the failure and throws. Retry catches it and re-enters Circuit Breaker for the next attempt.

This means:
- Retries count as individual attempts against the circuit breaker's failure ratio. Three failed retries can help trip the breaker, which is the right behaviour — repeated failures are a signal of genuine instability.
- If the circuit is already open, the Circuit Breaker throws `BrokenCircuitException` immediately. Retry catches that and retries — which would also hit an open circuit. The `ShouldHandle` predicate stops this: `BrokenCircuitException` is not `HttpRequestException`, so the retry policy does not fire. The exception propagates to the delegating handler.

## Idempotency

An operation is **idempotent** if running it multiple times produces the same result as running it once. A read is always safe to repeat. A write depends on what the server does with it: if the server produces the same outcome regardless of how many times the request arrives, it is idempotent; if each call triggers a new side effect, it is not.

Retry is only safe when the operation underneath is idempotent. The reason: if the first attempt reaches the server, does its work, but the response is lost in transit (TCP reset, timeout), you cannot tell from the client whether the work happened. A retry sends the request again. If the operation is idempotent, that is fine — the second call is a no-op or an acceptable duplicate. If the operation is not idempotent — a funds transfer, an order submission, an inventory decrement — the second call executes the side effect a second time, and you now have a problem that is difficult to detect and hard to undo.

Before applying retry to any HTTP call, confirm that repeating the request is safe.

## Timeout budget multiplication

Each retry attempt consumes time. `MaxRetryAttempts = 3` means 3 retries after the initial attempt — 4 total attempts. With a 3-second `HttpClient.Timeout`, the worst-case elapsed time before the final exception propagates is roughly `4 attempts × 3s = 12s`, plus backoff delays. The caller's request timeout must be larger than this budget, or the `CancellationToken` will cancel the pipeline mid-retry, which surfaces as a `TaskCanceledException` rather than a `NotificationsUnavailableException`.

Keep this in mind when sizing `TimeoutSeconds` in `NotificationsOptions` and any outer timeouts (e.g., the ASP.NET request pipeline or a load balancer's idle timeout).

## Implementation

### Resilience pipeline

Configured in `Program.cs` using `Microsoft.Extensions.Http.Resilience`:

```csharp
builder.Services.AddHttpClient("Notifications", ...)
    .AddHttpMessageHandler<NotificationsResilienceHandler>()
    .AddResilienceHandler("notifications-pipeline", resilienceBuilder =>
    {
        resilienceBuilder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = static args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                (args.Outcome.Result is { IsSuccessStatusCode: false } r && (int)r.StatusCode >= 500))
        });

        resilienceBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(15),
        });
    });
```

### Delegating handler

After all retry attempts are exhausted, a final `HttpRequestException` propagates up through the pipeline. `NotificationsResilienceHandler` catches it after the more-specific `BrokenCircuitException`:

```csharp
catch (BrokenCircuitException)
    => throw new NotificationsUnavailableException();

catch (HttpRequestException)
    => throw new NotificationsUnavailableException();
```

Both map to the same domain exception because from the caller's perspective the result is the same: the Notifications service could not be reached.

## Gotchas

- `BrokenCircuitException` must be caught before `HttpRequestException`. In Polly v8, `BrokenCircuitException` inherits from `Exception` directly — not from `HttpRequestException` — so the ordering has no effect on exception matching. The `ShouldHandle` predicate already excludes `BrokenCircuitException`, so the retry policy will not fire on an open circuit. But the catch ordering in `NotificationsResilienceHandler` should still place `BrokenCircuitException` before `HttpRequestException` as a defensive convention: specific before general.
- Retry amplifies load. Three attempts with five concurrent users becomes fifteen requests to the downstream service. If the service is already at capacity, this makes things worse. The circuit breaker is the safety valve: once enough attempts fail, the breaker opens and retries stop reaching the network.
- Do not retry on `TaskCanceledException`. If the user's request was cancelled or the outer timeout fired, retrying is pointless and wastes resources. The `ShouldHandle` predicate correctly excludes it.

## What's next

Retry handles transient errors; the circuit breaker handles sustained failures. Neither limits the number of concurrent requests hitting the downstream service. A bulkhead (rate limiter or concurrency limiter) would add that protection, preventing a slow or failing downstream service from consuming all available threads in `Todos.Web`.
