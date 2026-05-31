# Let It Crash

## What it is

Let It Crash is a principle about where recovery responsibility lives. Your code handles what it understands. Everything else propagates up and the infrastructure above your process handles it.

The principle comes from Erlang, where processes are cheap, isolated, and supervised. A crashed process is restarted automatically by a supervisor. The application stays available. The crash is a signal, not a disaster.

In .NET the model is structurally the same. Your process runs inside a supervisor — Docker, Kubernetes, systemd, IIS. When the process exits, the supervisor restarts it clean. Fresh memory, fresh state, no corruption carried forward.

## The rule

Only catch exceptions you can handle meaningfully. Let everything else propagate.

Before writing any catch block, anywhere in your code, ask these three questions:

1. Do I know exactly what caused this exception?
2. Is the application state still valid after it fired?
3. Do I have a response that makes sense — not just logging, an actual action?

If any answer is no — do not write the catch block. Let it propagate.

This is the tool you carry into every situation. Not a list of locations to check. Not a pattern that only applies to certain constructs. Every catch block, every time.

## What happens when you let it propagate

**At the request level.** ASP.NET Core catches the exception at the middleware boundary. The request fails. The process keeps running. Kestrel continues accepting new requests. Your global exception handler owns this level — it catches what it understands and lets everything else reach the boundary naturally.

**At the process level.** An exception propagates outside the request pipeline — a background service, a timer, a thread. The host stops. In production the supervisor sees the exit and restarts the process clean. Fresh memory, fresh state, no corruption carried forward. Users see a brief interruption. Your logs show exactly what happened.

You do not control what happens after the process exits. The supervisor does. Your only responsibility is to not interfere — do not write code that keeps a broken process alive.

## What happens when you don't

You catch an exception you do not understand and continue. The process keeps running. But whatever was executing did not finish. State might be half-written. A connection might be broken. A value might be wrong. You do not know.

The process keeps serving requests. It might keep producing wrong results. Nothing signals that anything is wrong. Hours later — maybe never — someone notices something is off. The logs show nothing useful because the exception was swallowed long ago.

A brief visible failure that recovers automatically is better than a silent invisible one that compounds.

## Applying the three questions — an example

A BackgroundService is a good vehicle for demonstrating this because it is where the wrong choice is most tempting. The service runs on a timer. Something throws. The instinct is to catch it and keep the service running. This is one example of applying the three questions — the same questions apply in a controller, a repository, an event handler, or anywhere else you write a catch block.

Run the three questions.

**Wrong — catch block that fails the questions:**

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await DoWorkAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong. Continuing.");
            // carry on
        }

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
    }
}
```

Question 1 — do I know what caused it? No. `Exception` is everything.
Question 2 — is the state still valid? Unknown. `DoWorkAsync` did not finish.
Question 3 — do I have a meaningful response? No. Logging and continuing is not a response.

All three fail. Do not write this catch block.

**Right — no catch block for what you don't understand:**

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await DoWorkAsync();
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
    }
}
```

If `DoWorkAsync` throws unexpectedly, the exception propagates to the host. The host stops. The supervisor restarts it clean.

**Right — catch block that passes the questions:**

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await DoWorkAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Downstream unavailable. Will retry next interval.");
        }

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
    }
}
```

Question 1 — do I know what caused it? Yes. The downstream HTTP call failed.
Question 2 — is the state still valid? Yes. Nothing was partially written.
Question 3 — do I have a meaningful response? Yes. Log, skip this interval, retry next time.

All three pass. This catch block is justified.

## The other trap — fire and forget

```csharp
Task.Run(() => DoSomething());
```

No await. No continuation. If `DoSomething` throws, the exception disappears completely. Not logged, not rethrown, not visible anywhere. The three questions never even get asked.

Always await tasks or attach a continuation that handles the exception explicitly. If you genuinely need fire and forget, the exception still needs to go somewhere.

## What if the crash keeps happening

The supervisor restarts the process every time it exits. If the bug is still there, the next instance crashes too. This sounds alarming. It is not — it is the pattern working correctly.

Every crash is logged. The exception appears in your logs every single time — not once, buried and forgotten. If your observability is set up correctly, you are alerted after the first crash. You see the exception immediately. You know exactly what to fix.

Supervisors also implement restart backoff. They do not hammer the process back up at full speed forever. Kubernetes for example waits 10 seconds, then 20, then 40, capping at 5 minutes between restarts. The service remains partially available between restarts. You have time to respond without the system destroying itself.

Compare this to the alternative. The broad catch block keeps the process running. No restart, no repeated log entry, no alert. The bug is still there. The state is corrupt. The system is running but wrong. You find out when a user reports bad data days later.

Repeated visible crashes with clear logs are easier to diagnose and fix than silent invisible corruption. The crashes are not the problem — they are the signal that leads you to the problem.

## How to audit a codebase

Search for these and run the three questions on every result:

- `catch (Exception` — does it pass all three questions?
- `catch { }` — always wrong, no questions needed
- Every `BackgroundService` — read `ExecuteAsync` — is there a broad catch inside the loop?
- Every `Task.Run` — is the result awaited?
- Every `_ = task` assignment — where does the exception go?

## In this app

`NotificationsResilienceHandler` catches `BrokenCircuitException`, `RateLimiterRejectedException`, `TaskCanceledException`, and `HttpRequestException`. Run the three questions on each — known cause, valid state, meaningful response. All pass. These are correct.

The global exception handler in `Todos.Web` catches everything at the request boundary. This is not recovery — it is presentation. It ends the request, logs, and returns an error to the user. The process moves on to the next request fresh. The three questions apply differently here — this is the boundary handler, not a domain catch block. Its job is to ensure no exception ever reaches the user as a raw 500.

`Todos.Notifications` has a `NotificationSummaryService` — a background service that logs a count of received notifications. It is driven by a `Channel<bool>` written to by `NotificationCounter.Increment()` on every incoming notification, so the service fires immediately each time a notification arrives rather than on a fixed interval. It has no catch block at all: `ReadAllAsync(stoppingToken)` completes the async stream when the host cancels the token, so `ExecuteAsync` returns normally on graceful shutdown without needing to handle `OperationCanceledException` explicitly. Any other exception propagates to the host, the host stops, and the supervisor restarts it clean.

The service has two demo flags in `appsettings.Development.json` that let you experience both sides of the choice:

- `SimulateFailure: true` — throws `InvalidOperationException("Summary state is corrupted.")` on every notification, simulating unexpected state corruption.
- `BroadCatch: true` — catches that exception broadly and logs `"Something went wrong. Continuing."`, simulating the anti-pattern. The process keeps running. The count never logs. No signal reaches the operator.
- `BroadCatch: false` (the correct path) — the exception propagates, the host stops, and the terminal shows exactly what went wrong.

Run Part 1 first (`SimulateFailure: true, BroadCatch: true`), then Part 2 (`BroadCatch: false`). The contrast is the lesson: a broad catch block does not make the system more resilient — it makes a broken process invisible.

> **These flags are demo-only.** `SimulateFailure` and `BroadCatch` exist to make this behaviour observable in a local run. In a real service you would drive a background worker from a message queue, a database polling loop, or a proper event bus — not from an in-memory channel coupled to a controller. A configuration flag that deliberately corrupts state and a catch block written purely to demonstrate the anti-pattern have no place in production code.

## What the supervisor does

In production your process runs inside a supervisor:

- **Docker** — `restart: unless-stopped` in your compose file
- **Kubernetes** — the kubelet watches your pod and restarts it on exit
- **systemd** — `Restart=on-failure` in your service unit
- **IIS** — Application Pool recycling

When your process exits after an unhandled exception, the supervisor starts a new process. Clean. The developer does not write the supervisor — it is already there as part of the deployment. Let It Crash says trust it instead of writing recovery code that keeps a broken process alive.

## What's next

The patterns so far — Timeout, Circuit Breaker, Retry, Bulkhead, Steady State, Fail Fast, and Let It Crash — cover failure at every level: individual requests, the process boundary, and degradation over time. Each pattern is reactive: something goes wrong and the system recovers.

The next pattern, Handshaking, is proactive. Before two services commit to exchanging work, they check whether the other can actually handle it. A service under pressure can decline new work before it becomes overwhelmed, rather than accepting it and failing partway through.
