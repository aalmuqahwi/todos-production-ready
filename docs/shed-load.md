# Shed Load

## What it is

Shed Load is the decision to actively reject work you cannot do well rather than accept it and do it badly. When the system is at or near capacity, new requests get a 503 Service Unavailable immediately — no queuing, no degraded processing, no thread exhaustion. The work is dropped at the door.

## Why it matters

The failure mode it prevents is well-defined: a service that accepts more work than it can handle queues requests, latency climbs, queues grow, memory pressure increases, and eventually everything fails — including requests that would have succeeded under lighter load.

Rejection is more honest and more useful than degradation. A 503 tells the caller something real: "I can't handle this right now." A slow, overloaded response tells the caller nothing useful and holds resources in both systems.

There is also a compounding effect. Without Shed Load, a traffic spike causes latency to climb. Higher latency means requests take longer. Longer requests hold threads longer. Threads fill up faster. The system degrades faster than the load grows — a non-linear collapse.

## The distinction from Bulkhead

The bulkhead in this app caps concurrent outbound calls from `Todos.Web` to `Todos.Notifications` — it protects the caller from a slow downstream consuming all its threads. Shed Load sits on the receiving end: `Todos.Notifications` decides it cannot take on more work right now. One pattern protects the caller; the other protects the callee from the caller.

## Where it belongs in this app

`Todos.Notifications` is the receiver of traffic from `Todos.Web` and the service that would suffer under load. A fixed window rate limiter on the `/notifications` endpoint rejects excess inbound requests before they consume processing resources.

A fixed window limiter (N requests per time period) is the right shape here. `Todos.Notifications` is not doing slow processing — it increments a counter and logs — so the concern is throughput spikes, not processing pile-ups.

## Implementation

**`Program.cs`**

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("notifications", limiter =>
    {
        limiter.Window = TimeSpan.FromSeconds(1);
        limiter.PermitLimit = 10;
        limiter.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
});
```

`QueueLimit = 0` is the key detail — requests are rejected immediately when the window is full, not queued. Queuing would delay the rejection and hold threads open, which is exactly what this pattern exists to prevent.

`RejectionStatusCode = 503` means the existing circuit breaker in `Todos.Web` detects sustained shedding automatically. No changes to the `ShouldHandle` predicate are needed — 5xx already triggers retry and, if sustained, trips the breaker.

**`NotificationsController.cs`**

```csharp
[EnableRateLimiting("notifications")]
public IActionResult Create(NotificationRequest request)
```

## What the UI shows

Shedding does not surface immediately in `Todos.Web`'s UI, and that is intentional.

`TodosController` sends the notification with `PostAsJsonAsync` and does not call `EnsureSuccessStatusCode()`. Notifications are optional — a todo creation should not fail because a notification could not be delivered. A shed 503 is silently dropped, the todo is still created, and the user sees nothing unusual.

The circuit breaker is the downstream signal. The retry policy treats 503 as a failure (it matches the `5xx` predicate in `ShouldHandle`), so sustained shedding accumulates failures in the circuit breaker's sampling window. Once the failure ratio crosses the threshold, the breaker opens and subsequent calls throw `BrokenCircuitException`. That exception is caught in `NotificationsResilienceHandler` and re-thrown as `NotificationsUnavailableException`, which the global exception handler redirects to `/Error?reason=unavailable` — "The notifications service is temporarily unavailable."

So the observable sequence under load is:
1. Requests flood in — some 200, some 503 — todos are created normally, excess notifications silently dropped
2. Failure ratio in the circuit breaker climbs
3. Breaker opens — user sees the unavailable error page on the next todo creation
4. After the break duration (`BreakDuration = 15s`), the breaker half-opens, load backs off, and normal operation resumes

The correct long-term fix is not `EnsureSuccessStatusCode()` — it is the Decoupling Middleware pattern. If `Todos.Web` puts the notification on a queue instead of calling `Todos.Notifications` synchronously, shedding becomes invisible to the user entirely: the todo is created, the notification is delivered when `Todos.Notifications` recovers, and the two services are no longer coupled at request time.

## Gotchas

- **`PermitLimit` must be sized against real throughput.** 10 per second is illustrative. In a real service, measure your baseline throughput and p99 processing latency before choosing a limit. Too low and you shed legitimate traffic; too high and the limiter offers no protection.
- **The limit is per process.** In a multi-instance deployment, each instance enforces its own window independently. Ten instances each permitting 10 requests per second means 100 requests per second reach your processing logic.
- **`QueueLimit = 0` is intentional.** A non-zero queue limit turns this into a throttle, not a shed. Queuing absorbs bursts but holds threads and memory. For Shed Load the queue must be zero.
- **503 interacts with the caller's resilience pipeline.** The retry policy in `Todos.Web` will retry on 503, which may trip the circuit breaker if shedding is sustained. This is the correct behaviour — the two patterns form a feedback loop that backs off load naturally.

## What's next

Shed Load rejects excess work at the boundary. Create Back Pressure takes this further: instead of silently dropping requests, the system signals its own capacity upstream so callers slow down before the limit is reached. Where Shed Load is reactive, Back Pressure is cooperative.
