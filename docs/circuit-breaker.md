# Circuit Breaker

## What it is

A circuit breaker is a stability pattern that stops you from hammering a dependency that has already told you it's struggling. When enough requests fail within a window, the breaker opens — all subsequent calls fail immediately without even trying. After a cooldown period, it allows a probe through. If that succeeds, it closes again; if not, it stays open.

The name comes from electrical engineering. A breaker trips not because the device is broken, but to protect the circuit from damage caused by continuing to push current through a fault.

## Why it matters

Without a circuit breaker, every caller keeps retrying a failing downstream service. That creates a feedback loop: the dependency is already struggling, and now it's also being flooded with retries. It can't recover. Meanwhile, your threads are tied up in in-flight requests that are going to fail anyway — which means your own service degrades alongside the one you depend on.

A circuit breaker breaks that loop. It fails fast when the downstream is already known to be unhealthy, protecting both services.

## Where it belongs in this app

The boundary is the HTTP call from `Todos.Web` to `Todos.Notifications`. The circuit breaker sits in that call path — specifically in the `HttpClient` resilience pipeline — and is invisible to the controller. The controller calls `PostAsJsonAsync`. If the circuit is open, the resilience layer throws a `BrokenCircuitException`, which the delegating handler translates into `NotificationsUnavailableException`, which the global exception handler routes to the error page.

## The three states

**Closed** — normal operation. Requests flow through. The breaker counts failures. If the failure ratio exceeds the threshold within the sampling window, it trips.

**Open** — all requests fail immediately with `BrokenCircuitException` without touching the network. The downstream service gets a break. After `BreakDuration`, the breaker moves to half-open.

**Half-open** — one probe request is allowed through. If it succeeds, the breaker closes and normal operation resumes. If it fails, the breaker opens again for another `BreakDuration`.

## Implementation

### Resilience pipeline

Configured in `Program.cs` using `Microsoft.Extensions.Http.Resilience`. The circuit breaker is the innermost strategy in the shared pipeline, after the concurrency limiter and retry:

```csharp
builder.Services.AddHttpClient("Notifications", ...)
    .AddHttpMessageHandler<NotificationsResilienceHandler>()
    .AddResilienceHandler("notifications-pipeline", resilienceBuilder =>
    {
        resilienceBuilder.AddConcurrencyLimiter(10);

        resilienceBuilder.AddRetry(new HttpRetryStrategyOptions { ... });

        resilienceBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(15),
        });
    });
```

These settings mean: within any 30-second window, if at least 3 requests have been made and more than 50% have failed, open the breaker for 15 seconds.

### Delegating handler

`NotificationsResilienceHandler` sits at the front of the pipeline and catches exceptions thrown by the resilience layer, translating them into domain exceptions before they reach the controller:

```csharp
catch (TaskCanceledException) when (!requestAborted)
    => throw new NotificationsTimeoutException();

catch (BrokenCircuitException)
    => throw new NotificationsUnavailableException();

catch (RateLimiterRejectedException)
    => throw new NotificationsUnavailableException();

catch (HttpRequestException)
    => throw new NotificationsUnavailableException();
```

The handler uses `IHttpContextAccessor` to read `RequestAborted` — this is how it distinguishes a timeout from a user abort, which both surface as `TaskCanceledException`. `BrokenCircuitException` is caught before `HttpRequestException` because specific exceptions come before general ones.

### Custom exceptions

`NotificationsTimeoutException` — the service did not respond in time.  
`NotificationsUnavailableException` — the circuit breaker is open.

These are plain exceptions, not HTTP status codes. They carry meaning specific to this integration; the global handler decides what to do with them.

### Global exception handler

`GlobalExceptionHandler` handles the two custom exceptions before the generic fallback:

```csharp
if (exception is NotificationsTimeoutException)
    => LogWarning + redirect to /Error?reason=timeout

if (exception is NotificationsUnavailableException)
    => LogWarning + redirect to /Error?reason=unavailable

else
    => LogError + redirect to /Error
```

Timeout and unavailable are logged as warnings, not errors, because they reflect upstream conditions rather than bugs in this service.

### Controller

The controller has no `try/catch`. It calls `PostAsJsonAsync` and returns a redirect. Any exception propagates to the global handler.

## Gotchas

- `MinimumThroughput` must be met before the breaker can trip. In development, where you may only send a handful of requests, the breaker might not open even when every request fails. This is by design — you don't want a single bad request in a low-traffic environment to take the circuit offline.
- The circuit state is per-process. In a multi-instance deployment, each instance has its own breaker. One instance can be in open state while another is closed. This is usually fine — each instance protects itself independently.
- `SamplingDuration` is a sliding window, not a fixed bucket. The failure ratio is evaluated continuously as requests come in.
- The delegating handler must be registered before the resilience handler in the pipeline (`.AddHttpMessageHandler<>()` before `.AddResilienceHandler()`). The pipeline executes outermost-first on the way out, so the resilience handler runs first, throws if the circuit is open, and the delegating handler catches it on the way back.

## What's next

The circuit breaker protects against cascading failures. What it doesn't do is retry transient errors before tripping. Adding a retry policy before the circuit breaker in the pipeline would handle short-lived network blips without counting them as failures. The order matters: retry wraps around the circuit breaker, so retries are counted as individual attempts against the failure ratio.
