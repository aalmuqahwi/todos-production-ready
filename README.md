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

# Terminal 3 (optional) — Test Harness, replaces Todos.Notifications
cd src/TestHarness
dotnet run
```

Open http://localhost:5000. Add a todo — Todos.Web calls Todos.Notifications in the background.

## Trying the failure scenarios

> **Two ways to trigger failures:** stop or disable the real service (below), or use the [test harness](docs/test-harnesses.md) to set the exact failure mode on demand. Both work — the harness gives you more control without tearing down running processes.

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

**Shed Load**
1. With both services running, flood `/notifications` with more than 10 requests in a second:
   ```bash
   for i in $(seq 1 25); do \
     curl -s -o /dev/null -w "%{http_code}\n" \
       http://localhost:5002/notifications \
       -X POST -H "Content-Type: application/json" -d '{}' & \
   done; wait
   ```
2. Observe a mix of 200 and 503 responses — exactly 10 succeed per window, the rest are shed immediately with no queuing delay
3. Wait a second and send a single request — the window resets and it returns 200 again

**Create Back Pressure**
1. With both services running, send a request that triggers shedding and inspect the response headers:
   ```bash
   for i in $(seq 1 15); do \
     curl -s -D - -o /dev/null \
       http://localhost:5002/notifications \
       -X POST -H "Content-Type: application/json" -d '{}' & \
   done; wait
   ```
2. On shed responses, observe `retry-after: 1` in the headers alongside the 503 status — the service signals exactly when the next window opens
3. To see the header in isolation on a single shed request, first exhaust the window with 10 fast requests, then:
   ```bash
   curl -si -X POST http://localhost:5002/notifications \
     -H "Content-Type: application/json" -d '{}' | grep -E "HTTP/|retry-after|content-type"
   ```

## Using the test harness

`Todos.TestHarness` is a fake HTTP server that replaces `Todos.Notifications` during manual testing. `Todos.Web` already points at `http://localhost:5002` by default — just run the harness instead of the real service and flip its behaviour at any time without restarting.

**Check the current mode**

```bash
curl http://localhost:5002/harness/behavior
```

**ok** — baseline; every POST to `/notifications` returns 200 immediately.

```bash
curl -s -X POST http://localhost:5002/harness/behavior \
     -H "Content-Type: application/json" \
     -d '{"mode":"ok"}'
```

**delay** — responds after `delaySeconds`. Set it above `TimeoutSeconds` in `appsettings.Development.json` to reliably trigger the timeout and watch retries exhaust.

```bash
curl -s -X POST http://localhost:5002/harness/behavior \
     -H "Content-Type: application/json" \
     -d '{"mode":"delay","delaySeconds":10}'
```

**error** — returns HTTP 500 on every call. Send several requests in a row to trip the circuit breaker.

```bash
curl -s -X POST http://localhost:5002/harness/behavior \
     -H "Content-Type: application/json" \
     -d '{"mode":"error"}'
```

**hang** — accepts the connection but never responds. Same observable effect as a long delay but exercises the read-timeout path rather than the write path.

```bash
curl -s -X POST http://localhost:5002/harness/behavior \
     -H "Content-Type: application/json" \
     -d '{"mode":"hang"}'
```

## Stability Patterns

- [x] Timeouts — [`docs/timeout.md`](docs/timeout.md)
- [x] Circuit Breaker — [`docs/circuit-breaker.md`](docs/circuit-breaker.md)
- [x] Bulkheads — [`docs/bulkhead.md`](docs/bulkhead.md)
- [x] Steady State — [`docs/steady-state.md`](docs/steady-state.md)
- [x] Fail Fast — [`docs/fail-fast.md`](docs/fail-fast.md)
- [x] Let It Crash — [`docs/let-it-crash.md`](docs/let-it-crash.md)
- [x] Handshaking — [`docs/handshaking.md`](docs/handshaking.md)
- [x] Test Harnesses — [`docs/test-harnesses.md`](docs/test-harnesses.md)
- [x] Decoupling Middleware — [`docs/decoupling-middleware.md`](docs/decoupling-middleware.md)
- [x] Shed Load — [`docs/shed-load.md`](docs/shed-load.md)
- [x] Create Back Pressure — [`docs/create-back-pressure.md`](docs/create-back-pressure.md)
- [ ] Governor
