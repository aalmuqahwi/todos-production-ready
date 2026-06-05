# Handshaking

## What it is

Handshaking is a protocol between a caller and a callee where the callee gets to
advertise its own capacity and readiness *before* the caller commits work to it.
Rather than the caller blindly sending requests and discovering failure after the
fact — through a timeout, a 500, or a hung connection — the callee actively
signals its own health, and the caller (or the infrastructure between them)
respects that signal.

The name comes from the TCP three-way handshake: before any data flows, both
sides negotiate whether they're ready to communicate. The stability pattern
applies the same idea at the application layer.

The canonical implementation is a health endpoint that goes well beyond "is the
process alive" to answer "am I ready to take your work right now." Those are
different questions with different answers, and conflating them is one of the
most common sources of deployment-time incidents.

## Liveness vs. readiness — the distinction that matters

Most systems expose a single `/health` endpoint. That's a start, but it blurs
two concepts that need to be kept separate.

**Liveness** answers: is this process running and not stuck in a deadlock or
infinite loop? A liveness check failing means the process should be killed and
restarted. It should be cheap — a trivial HTTP 200, maybe a check that the event
loop is still turning. If the liveness check itself is slow or resource-heavy,
you've made the problem worse.

**Readiness** answers: is this instance prepared to serve production traffic
right now? A readiness check failing means the instance should be temporarily
removed from the load balancer rotation, but not killed — it may be warming up,
draining, or waiting on a dependency. Readiness is the handshake.

A process can be alive but not ready: it just started and its connection pool
isn't warm, its caches aren't loaded, its first few calls to its own dependencies
haven't succeeded yet. Sending traffic to it in that state produces slow or
failed requests for users during what should be a seamless deploy.

A process can also be ready and then become not-ready: a downstream it depends
on went away, its database connection pool exhausted, its queue backed up beyond
a threshold. In those cases the instance should temporarily stop receiving new
traffic, not crash.

## Why it matters

Every other stability pattern in this series is reactive. Timeout bounds a wait
that's already happening. The circuit breaker detects failures and stops
repeating them. Retry absorbs transient errors after they occur. Bulkhead limits
damage once threads are already blocked.

Handshaking is the one proactive pattern. It stops the problematic request from
being sent in the first place, before the failure cascade has a chance to start.

The failure modes it prevents are real and common:

**Startup traffic before the service is ready.** A new instance comes up during
a rolling deploy. It binds its port, the load balancer health check passes the
liveness probe, and traffic starts flowing — but the service hasn't finished its
startup sequence. Connection pools aren't saturated, JIT compilation hasn't
warmed the hot paths, caches are empty. The first wave of users hits a slow or
broken instance. A readiness check that only passes after startup is genuinely
complete would have prevented this entirely.

**Cascading failure from a shared dependency.** Ten instances of a service all
depend on the same database. The database slows down — not enough to trigger a
connection timeout, but enough to make every query take ten times as long. Each
instance's threads start piling up waiting on database calls. Eventually threads
exhaust, requests queue, the load balancer's health checks start timing out, and
all ten instances are marked unhealthy simultaneously. Now nothing serves
traffic. A readiness check that monitors database query latency or connection
pool saturation would have started shedding individual instances from rotation
earlier, when recovery was still possible.

**Graceful drain during shutdown.** When a process receives SIGTERM, it should
stop accepting new connections while finishing the ones already in-flight.
Without handshaking, the load balancer keeps routing new requests to a process
that's about to die. With a readiness check that fails immediately on SIGTERM,
the load balancer removes the instance from rotation before the drain begins.

## Where handshaking lives

The primary home of the pattern is infrastructure — load balancers and
orchestrators. A Kubernetes readiness probe hits `/health/ready` on a schedule
(every few seconds). If it fails, the pod is removed from the Service's endpoint
list — no new traffic reaches it. When it recovers, it's added back
automatically. The application doesn't need to know this is happening; the
infrastructure handles it. AWS ALB, NGINX, HAProxy, and every serious load
balancer have equivalent mechanisms. This is where the pattern has the most
leverage: the health signal is consumed by something that can actually act on it
across the whole fleet.

## What a readiness check should actually check

A readiness check that always returns 200 is just a liveness check with a
different URL. For it to be meaningful, it needs to reflect the service's actual
ability to handle work. What to check depends on what the service needs to
function.

**Database connectivity and latency.** Not just "can I open a connection" but
"can I execute a trivial query within a tight timeout." A slow database is more
dangerous than a down one — it backs up threads without triggering connection
errors.

**Connection pool saturation.** If the pool is at 95% utilization, the service
is about to start queueing database calls. That's a readiness signal worth
exposing.

**Downstream dependencies.** Check the dependencies the service can't function
without. But be precise here — a readiness check that calls all your dependencies
can turn a partial outage into a full one. If the check itself has no timeout and
a dependency hangs, the readiness check hangs, every instance looks unhealthy,
and the load balancer removes everything from rotation. Health checks must be
bounded and cheap.

**Queue depth.** If the service processes from a queue, a backed-up queue means
it's falling behind. Exposing that as a degraded readiness state lets the
scheduler make better routing decisions.

**Startup completion.** A simple boolean flag set at the end of the startup
sequence. The readiness check returns 503 until the flag is true. This alone
prevents the startup-traffic problem described above.

What *not* to check: things that are optional, degraded but functional, or
handled by another pattern. If the service can operate without a cache (slower,
but correctly), a cold cache is not a readiness failure. If the circuit breaker
is open on a non-critical dependency, that may not affect readiness either.
Readiness should mean "I cannot handle requests correctly right now," not "I am
not at 100%."

## ASP.NET Core health checks

ASP.NET Core ships `Microsoft.Extensions.Diagnostics.HealthChecks` as a
first-class feature. The API is designed around exactly this liveness/readiness
distinction.

```csharp
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseReadinessCheck>("database", tags: ["ready"])
    .AddCheck<DependencyReadinessCheck>("notifications", tags: ["ready"]);

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

The `live` endpoint answers the liveness question — Kubernetes points its
liveness probe here. The `ready` endpoint answers the readiness question —
Kubernetes points its readiness probe here, and the load balancer uses it to
gate traffic. The tag-based filtering means a single registration drives both
endpoints with no duplication.

Custom checks implement `IHealthCheck`:

```csharp
public class DatabaseReadinessCheck : IHealthCheck
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DatabaseReadinessCheck(IDbConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.Create();
            await connection.ExecuteAsync(
                "SELECT 1",
                commandTimeout: 2, // tight timeout — the check must not hang
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
```

The `AspNetCore.HealthChecks.*` NuGet ecosystem provides pre-built checks for
SQL Server, PostgreSQL, Redis, RabbitMQ, HTTP endpoints, and more — so you
rarely need to write the low-level check yourself.

## Why this app doesn't implement it

This app is a single-instance setup with no load balancer, no orchestrator, and
no mechanism to act on a health signal even if one were emitted. Adding
`/health/live` and `/health/ready` endpoints would be technically correct but
hollow — nothing queries them, nothing routes around an unhealthy instance,
nothing restarts a stuck process.

More concretely: the circuit breaker in `Todos.Web` already serves the
caller-level handshake. It observes failures from `Todos.Notifications` and
stops routing to it when enough have accumulated. A pre-flight health check
before each notification call would add a round-trip and duplicate that
detection. The infrastructure-level handshake — readiness probes driving load
balancer routing — has no infrastructure to land in.

The pattern is documented here because it has a clear, correct home in any
production deployment of this app. The moment `Todos.Notifications` runs behind
a load balancer with multiple instances, readiness probes become the right tool
for preventing the startup-traffic problem and for graceful drain during rolling
deploys. The ASP.NET Core health check infrastructure is low-friction to add —
a NuGet package, a few registrations, two mapped endpoints — and it pays for
itself the first time a deploy goes wrong without it.

## What's next

The next pattern is Test Harnesses — a way to simulate the failure conditions
that the patterns above are designed to handle. Timeouts, circuit breakers, and
retries are only as reliable as your ability to test them; a test harness gives
you controlled, repeatable ways to inject latency, errors, and unresponsive
dependencies without touching production systems.