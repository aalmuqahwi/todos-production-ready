# Bulkhead

## What it is

A bulkhead limits how many concurrent requests can be in-flight to a given dependency at once. When the limit is reached, new requests are rejected immediately rather than queued up waiting for a thread.

The name comes from ship design — a hull divided into watertight compartments so flooding one section doesn't sink the vessel.

## Why it matters

The circuit breaker handles a *stopped* dependency. A slow one is more dangerous: requests are technically succeeding, so nothing trips. But every thread in `Todos.Web` is blocked waiting on `Todos.Notifications`. New requests arrive, grab threads, also block. Eventually there are no threads left to serve anything — including routes that have nothing to do with notifications.

The bulkhead caps how many threads can be in that blocked state simultaneously. Requests above the cap fail fast. The app stays responsive.

## Where it belongs in this app

In the `HttpClient` resilience pipeline in `Todos.Web`, as the outermost strategy — before retry and circuit breaker. A concurrency limiter is the right shape here: you're capping simultaneous in-flight requests, not rate-limiting over time.

It is worth being precise about what this protects: the bulkhead shields `Todos.Web` from a slow `Todos.Notifications` — it does nothing for `Todos.Notifications` itself. If the concern is protecting `Todos.Notifications` from being hammered by `Todos.Web`, that is a different problem and a different pattern: a rate limiter on the receiver, which in the book's terms falls under Shed Load.

## Pipeline order
Concurrency Limiter → Retry → Circuit Breaker → Network

Outermost means the limit is enforced on the caller's attempt, not on each individual retry. Inside retry, one user request could hold multiple slots simultaneously during retries — defeating the purpose.

## Implementation

**`Program.cs`**

```csharp
resilienceBuilder.AddConcurrencyLimiter(10);
```

Added as the first strategy in the pipeline.

**`NotificationsResilienceHandler.cs`**

```csharp
catch (RateLimiterRejectedException)
    => throw new NotificationsUnavailableException();
```

When the limit is exceeded, Polly throws `RateLimiterRejectedException`. The handler translates it to `NotificationsUnavailableException` — same domain exception as an open circuit, because from the caller's perspective the result is the same: the service couldn't be reached right now.

## Gotchas

- **Bulkhead protects the caller, not the dependency.** It shields your thread pool from a slow downstream. Protecting the downstream from too much inbound traffic is a different pattern — Shed Load.
- **The limit is per process.** In a multi-instance deployment, each instance enforces its own limit independently. Ten instances each with a limit of 10 means 100 concurrent requests can reach `Todos.Notifications`.
- **Size it against your thread pool and downstream latency.** Too low and you reject legitimate traffic. Too high and the bulkhead doesn't protect you. Base it on `p99 latency × expected concurrency`, not a guess.

## What's next

Bulkhead, circuit breaker, retry, and timeout now form a complete defensive layer around the `Todos.Notifications` boundary. The next patterns shift focus — Steady State is about keeping a system running cleanly over time rather than surviving individual failures.
