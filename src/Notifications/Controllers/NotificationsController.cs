using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using Todos.Notifications.Models;
using Todos.Notifications.Services;

namespace Todos.Notifications.Controllers;

/// <summary>Receives notification events from upstream services.</summary>
[ApiController]
[Route("[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly NotificationCounter _counter;

    public NotificationsController(NotificationCounter counter)
    {
        _counter = counter;
    }

    /// <summary>Processes an incoming notification.</summary>
    [HttpPost]
    [EnableRateLimiting("notifications")]
    public IActionResult Create(NotificationRequest request)
    {
        _counter.Increment();
        return Ok();
    }
}
