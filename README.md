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

## Stability Patterns

- [x] Timeouts — [`docs/timeout.md`](docs/timeout.md)
- [x] Circuit Breaker — [`docs/circuit-breaker.md`](docs/circuit-breaker.md)
- [x] Bulkheads — [`docs/bulkhead.md`](docs/bulkhead.md)
- [x] Steady State — [`docs/steady-state.md`](docs/steady-state.md)
- [x] Fail Fast — [`docs/fail-fast.md`](docs/fail-fast.md)
- [ ] Let It Crash
- [ ] Handshaking
- [ ] Test Harnesses
- [ ] Decoupling Middleware
- [ ] Shed Load
- [ ] Create Back Pressure
- [ ] Governor
