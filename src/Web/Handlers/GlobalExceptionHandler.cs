using Microsoft.AspNetCore.Diagnostics;

using Todos.Web.Exceptions;

namespace Todos.Web.Handlers;

/// <summary>Handles unhandled exceptions by redirecting to the error page.</summary>
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

        if (exception is NotificationsTimeoutException) // docs/timeout.md
        {
            _logger.LogWarning(exception, "Notifications service timed out.");
            context.Response.Redirect("/Error?reason=timeout");
        }
        else if (exception is NotificationsUnavailableException) // docs/circuit-breaker.md, docs/bulkhead.md
        {
            _logger.LogWarning(exception, "Notifications service circuit breaker is open.");
            context.Response.Redirect("/Error?reason=unavailable");
        }
        else // docs/global-exception-handling.md
        {
            _logger.LogError(exception, "An unhandled exception occurred.");
            context.Response.Redirect("/Error");
        }

        return ValueTask.FromResult(true);
    }
}
