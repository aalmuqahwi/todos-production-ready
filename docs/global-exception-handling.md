# Global Exception Handling

## What it is

Global exception handling is a catch-all for unhandled exceptions — the ones that escape every try/catch in your application and would otherwise surface as a raw 500 with a stack trace, or silently swallow and leave the caller hanging. You register one handler, and it owns everything the rest of your code didn't explicitly handle.

## Why it matters

Without it, an unhandled exception produces whatever ASP.NET Core's default behavior is for your app type — usually an empty 500 in a Web API, or a developer exception page that leaks internals in an MVC app. Neither is acceptable in production. You want a consistent, safe response on every error path, and you want the exception logged before the response goes out.

It also separates error presentation from error handling. Controllers and services don't need to know what an error page looks like, or what the right HTTP status is for an unexpected exception. That's the handler's job.

## Where it belongs in this app

This app has two surfaces:

- **`Todos.Web`** is an MVC app serving HTML. Unhandled exceptions should redirect to a user-facing error page — not return a JSON body.
- **`Todos.Notifications`** is an API. Unhandled exceptions should return a `ProblemDetails` response with status 500 — not an HTML page.

Both register a `GlobalExceptionHandler` implementing `IExceptionHandler`, but the response strategy differs.

## Implementation

### MVC (`Todos.Web`)

**`Handlers/GlobalExceptionHandler.cs`**

```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        if (exception is NotificationsTimeoutException)
        {
            _logger.LogWarning(exception, "Notifications service timed out.");
            context.Response.Redirect("/Error?reason=timeout");
        }
        else if (exception is NotificationsUnavailableException)
        {
            _logger.LogWarning(exception, "Notifications service circuit breaker is open.");
            context.Response.Redirect("/Error?reason=unavailable");
        }
        else
        {
            _logger.LogError(exception, "An unhandled exception occurred.");
            context.Response.Redirect("/Error");
        }

        return ValueTask.FromResult(true);
    }
}
```

Returning `false` when the request is already aborted means ASP.NET Core keeps looking for another handler — which is fine here, since the client is gone and there's nothing useful to write.

Known downstream exceptions (`NotificationsTimeoutException`, `NotificationsUnavailableException`) are logged as warnings and routed to a specific error query-string so the error page can show a contextual message. Everything else falls through to the generic error path.

**`Controllers/ErrorController.cs`** routes `/Error` to a Razor view so the error page goes through the normal MVC pipeline.

**`Program.cs`**

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

app.UseExceptionHandler();
app.UseStaticFiles();
```

`AddProblemDetails()` is required even in MVC — `UseExceptionHandler()` depends on the problem details infrastructure to function.

### API (`Todos.Notifications`)

**`Handlers/GlobalExceptionHandler.cs`**

```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) =>
        _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An unhandled exception occurred.");

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = "An unexpected error occurred."
        };

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
```

Always returns `true` — an API has no fallback surface. Every unhandled exception gets a 500 ProblemDetails body.

**`Program.cs`**

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

app.UseExceptionHandler();
app.MapControllers();
```

## Gotchas

- **`UseExceptionHandler()` must come early in the pipeline.** It wraps the remainder — register it before any middleware that could throw, such as `UseStaticFiles()` and `UseRouting()`.
- **`AddProblemDetails()` is not optional.** Even when you're not writing a ProblemDetails response yourself, the exception handler middleware requires it to be registered.
- **`TryHandleAsync` returning `false` is not an error.** It means "I'm not handling this one, keep looking." Use it for cases where writing a response is pointless — user abort, already-started response, etc.
- **Don't catch `OperationCanceledException` in the handler.** ASP.NET Core handles request abort cleanly on its own. Intercepting it just adds noise.
- **The detail field in ProblemDetails should not expose internals.** Log the full exception; return a generic message. Stack traces and inner exception messages are for your logs, not your callers.

## What's next

Global exception handling is a safety net, not a substitute for explicit handling. Specific exception types — validation failures, not-found cases, downstream timeouts — should still be caught where they occur and mapped to the right status code. The handler owns what nothing else claimed.

Retry and circuit breaker patterns will reduce how often unhandled exceptions reach here from the `HttpClient` path. Once those are in place, the handler's scope narrows further.
