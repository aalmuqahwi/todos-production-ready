using Microsoft.AspNetCore.Diagnostics;

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
        _logger.LogError(exception, "An unhandled exception occurred.");

        if (context.RequestAborted.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        context.Response.Redirect("/Error");

        return ValueTask.FromResult(true);
    }
}
