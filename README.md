# Todos — Production Ready

A working ASP.NET Core app progressively hardened with stability patterns from
*Release It!* by Michael Nygard. Each pattern is implemented in the codebase
and documented in `/docs`.

The app is intentionally minimal — a todo list calling a notifications service —
so the patterns aren't buried in domain logic.

## Prerequisites

- .NET 8 SDK

## Projects

- **Todos.Web** — ASP.NET Core MVC. Single-user todo app. Calls Todos.Notifications on every todo creation.
- **Todos.Notifications** — ASP.NET Core Web API. Receives notification events from Todos.Web.

## Running locally

```bash
# Terminal 1 — Notifications service
cd src/Notifications
dotnet run

# Terminal 2 — Web app
cd src/Web
dotnet run
```

Open http://localhost:5000. Add a todo — Todos.Web calls Todos.Notifications in the background.

## Trying the failure scenarios

The resilience pipeline is the interesting part. To see it in action:

**Timeout & Circuit Breaker**
1. Start Todos.Web but leave Todos.Notifications stopped
2. Add a todo — the request times out and retries exhaust
3. Add a few more — the circuit breaker trips and subsequent adds fail immediately without hitting the network
4. The error page shows a contextual message depending on the failure type

**Bulkhead**
1. With both services running, flood Todos.Web with concurrent requests
2. Once the concurrency limit is reached, excess requests are rejected immediately rather than queued

**Fail Fast**
1. In `appsettings.Development.json`, set `Notifications.BaseUrl` to an invalid value (e.g. `"not-a-url"`)
2. Start Todos.Web — it refuses to start with a clear validation error rather than failing at runtime

**Steady State**
1. Run either service for a while and check the `logs/` directory
2. Log files rotate daily and older files are deleted automatically — no manual cleanup needed

**Let It Crash**

> **Demo only.** `NotificationSummaryService`, `SimulateFailure`, and `BroadCatch` exist purely to make
> this behaviour observable in a local run. Do not carry any of them into production code.

**Part 1 — the wrong way (broad catch)**
1. In `src/Notifications/appsettings.Development.json` set `SimulateFailure: true` and `BroadCatch: true`
2. Start both services
3. Add a todo — the background service fires immediately, hits the simulated failure, and swallows it
4. Observe the Todos.Notifications terminal: the process keeps running, the count never logs, only a vague `"Something went wrong. Continuing."` appears — no signal that anything is broken

**Part 2 — the right way (let it crash)**
1. Keep `SimulateFailure: true`, set `BroadCatch: false`
2. Restart Todos.Notifications
3. Add a todo — the background service fires and throws
4. Observe the host stop with a clear `InvalidOperationException: Summary state is corrupted.` in the terminal — an unambiguous signal
5. Set `SimulateFailure: false`, restart, add another todo — the service logs the count cleanly

## Stability Patterns

- [x] Timeouts — [`docs/timeout.md`](docs/timeout.md)
- [x] Circuit Breaker — [`docs/circuit-breaker.md`](docs/circuit-breaker.md)
- [x] Bulkheads — [`docs/bulkhead.md`](docs/bulkhead.md)
- [x] Steady State — [`docs/steady-state.md`](docs/steady-state.md)
- [x] Fail Fast — [`docs/fail-fast.md`](docs/fail-fast.md)
- [x] Let It Crash — [`docs/let-it-crash.md`](docs/let-it-crash.md)
- [ ] Handshaking
- [ ] Test Harnesses
- [ ] Decoupling Middleware
- [ ] Shed Load
- [ ] Create Back Pressure
- [ ] Governor
