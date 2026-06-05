using System.Threading.RateLimiting;

using Polly.CircuitBreaker;
using Polly.RateLimiting;

using Todos.Web.Exceptions;

namespace Todos.Web.Handlers;

/// <summary>Translates resilience exceptions from the Notifications HTTP pipeline into domain exceptions.</summary>
// docs/test-harnesses.md
public class NotificationsResilienceHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public NotificationsResilienceHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!(_httpContextAccessor.HttpContext?.RequestAborted.IsCancellationRequested ?? false))
        {
            throw new NotificationsTimeoutException(); // docs/timeout.md
        }
        catch (BrokenCircuitException)
        {
            throw new NotificationsUnavailableException(); // docs/circuit-breaker.md
        }
        catch (RateLimiterRejectedException)
        {
            throw new NotificationsUnavailableException(); // docs/bulkhead.md
        }
        catch (HttpRequestException)
        {
            throw new NotificationsUnavailableException(); // docs/retry.md
        }
    }
}
