# Test Harnesses

## What it is

A test harness is a controllable stand-in for a real dependency. Not a mock inside a test process — a real network-accessible service that you own and can instruct to misbehave on demand. Your actual `HttpClient`, with its actual timeout and its actual Polly pipeline, makes a real TCP connection to it. Everything between your code and the dependency behaves exactly as it would in production.

## Why it matters

Your resilience pipeline exists to handle failure. But you can't verify it works unless you can produce the failures it's designed to handle. A real dependency either works or it's down — it doesn't hang for exactly 4 seconds, return 500 on every third request, or accept a connection and go silent. Those are the failure modes that expose incorrect assumptions in your resilience configuration, and they're nearly impossible to provoke reliably against a real service.

Without a harness, your resilience pipeline is a hypothesis. The retry configuration looks right. The `ShouldHandle` predicate seems correct. The timeout arithmetic adds up on paper. A harness is what converts "seems correct" into "demonstrably correct."

## The failure modes worth testing

Not all failures are equally interesting. The easy ones — connection refused, immediate 500 — confirm basic error translation. The dangerous ones are at the edges.

**Hang.** The dependency accepts the connection but never writes a byte. This is the failure your timeout exists for. Does the timeout fire? Does it fire at the right threshold? Does the exception propagate correctly through the pipeline?

**Slow response.** The dependency responds, but slowly — just under the timeout. Three retries at 2.9 seconds each on a 3-second timeout means 8.7 seconds of elapsed time before the final failure. Does your outer request budget accommodate this, or does `CancellationToken` fire mid-retry and surface as a `TaskCanceledException` your handler doesn't recognize?

**Sustained 500s.** Every request fails. Tests that retry exhausts correctly and that the circuit breaker trips after the configured failure ratio — and that subsequent requests fail immediately without touching the network.

**Transient 500s.** The first N requests fail, then succeed. Tests that retry fires, that the circuit breaker doesn't trip prematurely, and that eventual success propagates correctly.

**Wrong status codes.** A 422 should never be retried — it signals a client-side error that will never succeed on a second attempt. If someone edits the `ShouldHandle` predicate and accidentally includes 4xx responses, the retry policy starts hammering the dependency with requests it has already rejected. A harness with a "return 422, assert no retries" scenario catches that regression immediately.

## Where it belongs in this app

`Todos.TestHarness` is a third project in the solution that replaces `Todos.Notifications` during manual testing. It listens on the same port and accepts the same requests — `Todos.Web` doesn't know the difference.

As the app grows — Payments, Inventory, whatever — add a controller per downstream service in the same project. If a downstream service has multiple controllers in production (say, Payments splits charge and status across two controllers), mirror that in the harness. You only need to fake the endpoints your app actually calls, not the full surface of the real service. One project covers every downstream boundary.

## Gotchas

- **The harness is a dev tool.** No authentication, no persistence, no production use.
- **The mode is global, not per-request.** All in-flight requests see the same behavior. For concurrent scenarios, be aware that changing mode mid-test affects everything in flight.
- **Hang mode holds connections open.** Under load this can exhaust harness threads — that's expected and intentional. It's simulating exactly the condition the bulkhead exists to handle.
- **The harness doesn't replace automated tests.** It makes manual scenarios repeatable. If you want regression coverage — "a 422 must never be retried" — you need a test project that drives both `Todos.Web` and the harness programmatically and asserts on the outcome.

## What's next

The next pattern is Decoupling Middleware: instead of calling `Todos.Notifications` synchronously over HTTP, the two services communicate through a message queue. The caller fires and forgets; the dependency processes in its own time. A slow or unavailable `Todos.Notifications` no longer blocks `Todos.Web` at all — the coupling between their request lifecycles is removed entirely.