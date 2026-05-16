using Polly.CircuitBreaker;

using Todos.Web.Exceptions;

namespace Todos.Web.Handlers;

/// <summary>Translates resilience exceptions from the Notifications HTTP pipeline into domain exceptions.</summary>
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
            throw new NotificationsTimeoutException();
        }
        catch (BrokenCircuitException)
        {
            throw new NotificationsUnavailableException();
        }
    }
}
