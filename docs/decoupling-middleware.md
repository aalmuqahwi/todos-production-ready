# Decoupling Middleware

## What it is

Decoupling Middleware replaces synchronous, point-to-point calls between services with an intermediary — a message broker — that holds messages and delivers them independently of the sender. Instead of `Todos.Web` calling `Todos.Notifications` directly over HTTP, it drops a message onto a queue and moves on. `Todos.Notifications` picks that message up and processes it in its own time.

The "middleware" in the name refers to the broker infrastructure: RabbitMQ, Azure Service Bus, AWS SQS, Kafka. The broker sits between the two services and owns message delivery.

## Why it matters

Right now the coupling between `Todos.Web` and `Todos.Notifications` is temporal: both services must be alive, reachable, and responsive *at the same moment* for the operation to succeed. Every pattern applied so far — timeout, retry, circuit breaker, bulkhead — exists because of that coupling. They're workarounds for a fundamental problem: the caller is blocked waiting for the callee.

Decoupling Middleware removes that requirement entirely. The two services no longer need to coexist in time.

What this changes concretely:

- `Todos.Web` publishes a message and returns immediately. It never waits for `Todos.Notifications`.
- If `Todos.Notifications` is down, the message sits in the queue. When the service recovers, it processes the backlog. Nothing is lost.
- If `Todos.Notifications` is slow, it doesn't matter — `Todos.Web` has already moved on.
- Throughput spikes in `Todos.Web` don't translate into throughput spikes in `Todos.Notifications`. The queue absorbs the burst; the consumer processes at its own pace.

## What it changes about the existing resilience pipeline

The timeout, circuit breaker, retry, and bulkhead protecting the `Todos.Notifications` HTTP call become largely unnecessary for the notification path. Publishing to a managed queue is orders of magnitude more reliable than calling a business service — the failure modes are narrower, the latency is lower, and the broker itself handles delivery guarantees.

The Polly pipeline doesn't disappear — it applies to the queue publish call, which is now the boundary — but it becomes thin. The queue is the stability mechanism now.

## The tradeoffs

**You lose synchronous confirmation.** With HTTP, `Todos.Web` knows `Todos.Notifications` processed the notification because the response said 200. With a queue, `Todos.Web` knows only that the broker accepted the message. Whether it was processed — and when — is no longer knowable at the point of publish. For notifications this is fine: they're fire-and-forget by nature. For operations that need a response — a charge that must succeed before an order is confirmed — asynchronous messaging requires more complex patterns: request/reply, correlation IDs, callbacks.

**You gain operational complexity.** A message broker is a new infrastructure dependency. It needs to run, be monitored, be sized, have its queues managed. Dead-letter queues need attention when messages fail processing. Message schemas need versioning when they change. Ordering guarantees — or their absence — need to be understood and designed around.

## Why this app doesn't implement it

This app is a single-instance, single-user setup with no broker infrastructure and no operational environment to run one in. Adding RabbitMQ via Docker, wiring up MassTransit, and managing a queue would introduce significant infrastructure overhead for a pattern whose value only becomes apparent at scale — multiple instances, real concurrency, and a `Todos.Notifications` that actually needs to be insulated from `Todos.Web`'s request lifecycle.

The existing HTTP path with its Polly pipeline is also sufficient for a single-user app. The resilience patterns already handle the failure modes that matter here. Decoupling Middleware would replace them with something more powerful but also more complex, for no observable benefit at this scale.

The pattern is documented because it is the right architectural answer when the app grows: multiple instances behind a load balancer, a `Todos.Notifications` that needs to process at its own pace, or a requirement that notifications survive a service restart without being lost. At that point the HTTP call and its resilience pipeline become the wrong tool, and a queue becomes the obvious one.

## Where it belongs in this app

The publish would replace the `PostAsJsonAsync` call in `TodosController.Create`. The consume would live in a `BackgroundService` in `Todos.Notifications` that reads from the queue — replacing the HTTP controller as the primary ingestion path. The existing HTTP endpoint can remain for the test harness.

## Implementation shape

For a local setup: RabbitMQ via Docker with MassTransit as the client library. MassTransit handles the low-level broker interaction and gives you a clean publish/consume model without writing against the raw AMQP client.

The broad strokes:

- `Todos.Web` publishes a `TodoCreated` message via MassTransit
- `Todos.Notifications` registers a MassTransit consumer that handles `TodoCreated`
- MassTransit manages queue declaration, serialization, and retry on the consume side
- The Polly pipeline on `HttpClient` is retained but becomes a thin safety net rather than the primary reliability mechanism

## Reliable publish — the outbox pattern

Introducing a queue creates a new atomicity problem. Publishing a message and writing to a database are two separate operations. If the todo is saved but the publish fails — a broker blip, a process crash between the two lines — the todo exists but the notification never goes out. Flipping the order creates the same gap in reverse.

The outbox pattern solves this. Instead of publishing to the broker directly, you write the message to an `OutboxMessages` table in the same database transaction as the domain write. A separate background processor reads unpublished messages from that table and publishes them to the broker, then marks them as published.

The domain operation is now atomic: either the todo and the outbox message are both written, or neither is. The broker publish is eventually guaranteed — the processor retries until it succeeds.

In DDD and clean architecture terms this fits naturally. Domain events are raised inside the aggregate, persisted via the outbox as part of the same unit of work, and dispatched to the outside world by infrastructure. The domain stays pure; the messaging concern lives in the outer layer.

## Pragmatic alternative — Hangfire

If a full broker and outbox feel like too much infrastructure for your context, Hangfire is a legitimate middle ground. Hangfire persists jobs to a database before they run, so if the process crashes before the job executes, it will be picked up and retried on restart. You get durability without operating a broker.

The caveats: atomicity still isn't guaranteed unless you enroll the Hangfire enqueue in the same database transaction as your domain write (which Hangfire supports when both share the same database). And Hangfire is a job scheduler, not a message broker — you don't get pub/sub, multiple consumers, or message routing. For a notification side-effect in a contained app it's a reasonable pragmatic choice. For genuinely decoupling two services at scale, it's the wrong tool.

## What's next

Decoupling Middleware removes the temporal coupling between services. The next patterns shift focus to capacity under load: Shed Load is about actively rejecting excess work when the system is at its limit, rather than accepting it and degrading.
