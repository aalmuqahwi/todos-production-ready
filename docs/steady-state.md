# Steady State

## What it is

Steady State is the observation that systems fail over time not from a single dramatic event, but from gradual accumulation — log files that fill a disk, caches that grow without bound, queues that back up and never drain. The system worked at launch. It degraded slowly, invisibly, until something tipped.

The pattern is: every mechanism that accumulates must have a corresponding mechanism that drains it. Accumulation without drainage is a slow-motion failure.

## Why it matters

These failures don't show up in testing. Your integration tests don't run for six weeks. A disk fills, a process crashes, and the post-mortem finds a log directory with 40GB of unrotated files. Nothing was broken — there was just no drain.

## Where it belongs in this app

Steady State doesn't belong to a single boundary — it applies wherever something accumulates. In this app there are three points worth naming.

Log output is the most concrete. Both projects write continuously, and without a configured drain that output grows until something breaks. This is what the implementation addresses.

The `_todos` list in `TodosController` illustrates a common accumulation pattern. Static in-memory collections are often used for caching, lookup tables, or shared state — and they grow for the lifetime of the process with nothing to drain them. In production that means unbounded memory growth. Any static collection that accumulates entries needs an eviction policy, a TTL, or a size cap.

The circuit breaker and concurrency limiter maintain internal state — failure counts, active request counters. These are bounded by their configuration: the circuit breaker resets on the sampling window, the concurrency limiter rejects at the cap rather than queuing. The drain is built in, but it's still accumulation worth being conscious of.

## What to audit as the app grows

Every new integration point is a potential accumulation point. The question to ask at every boundary: what accumulates here, and what drains it?

- **Database** — rows without a retention or archival policy grow forever
- **Queue** — messages that can't be consumed back up; dead-letter queues need their own drain
- **Session store** — sessions that are never expired hold memory indefinitely
- **Temp files** — any file written during request processing needs cleanup on completion or failure

## Implementation

Log files are the accumulation point; Serilog with rolling file output is the drain. Both projects replace the default ASP.NET Core logger with Serilog, configured to rotate daily and delete files beyond the retention window. The sink and the drain are configured together — one without the other is incomplete.

**`Program.cs`** (both projects)

```csharp
builder.Host.UseSerilog((context, _, config) =>
    config.ReadFrom.Configuration(context.Configuration));
```

**`appsettings.json`** — production defaults, 30-day retention

```json
"Serilog": {
  "MinimumLevel": "Information",
  "WriteTo": [
    { "Name": "Console" },
    {
      "Name": "File",
      "Args": {
        "path": "/var/log/todos-web-.log",
        "rollingInterval": "Day",
        "retainedFileCountLimit": 30
      }
    }
  ]
}
```

**`appsettings.Development.json`** — local paths, shorter retention, debug level

```json
"Serilog": {
  "MinimumLevel": "Debug",
  "WriteTo": [
    { "Name": "Console" },
    {
      "Name": "File",
      "Args": {
        "path": "logs/todos-web-.log",
        "rollingInterval": "Day",
        "retainedFileCountLimit": 7
      }
    }
  ]
}
```

The `logs/` directory is gitignored. The production path and retention window can be overridden per environment without a redeploy:
Serilog__WriteTo__1__Args__path=/custom/path/todos-web-.log
Serilog__WriteTo__1__Args__retainedFileCountLimit=14

## Gotchas

- `retainedFileCountLimit` is not optional. A rolling sink without a retention limit still accumulates — it just does it in smaller files.
- The retention window must be sized to the environment. 30 days assumes the disk can hold 30 days of log volume at real production throughput. Audit log size under load before committing to a window.
- `/var/log/` on Linux requires write permission for the process user. The directory must be created and owned appropriately before the app starts.

## What's next

The stability patterns so far — Timeout, Circuit Breaker, Retry, Bulkhead, and Steady State — form a complete defensive layer around the `Todos.Notifications` boundary and keep the system healthy over time. The next set of patterns shifts focus to capacity: making sure the system can handle load without degrading, and shedding excess load gracefully when it can't.