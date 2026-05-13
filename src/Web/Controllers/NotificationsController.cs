using Microsoft.AspNetCore.Mvc;

namespace Todos.Web.Controllers;

/// <summary>Proxies requests to the Notifications downstream service.</summary>
[Route("[controller]")]
public class NotificationsController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NotificationsController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Returns notifications from the downstream service.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient("Notifications");

        try
        {
            HttpResponseMessage response = await client.GetAsync("/notifications", cancellationToken);
            string content = await response.Content.ReadAsStringAsync(cancellationToken);

            return Content(content, "application/json");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(
                detail: "The Notifications service did not respond in time.",
                title: "Gateway Timeout",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}
