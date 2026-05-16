using Microsoft.AspNetCore.Mvc;

using Todos.Notifications.Models;

namespace Todos.Notifications.Controllers;

/// <summary>Receives notification events from upstream services.</summary>
[ApiController]
[Route("[controller]")]
public class NotificationsController : ControllerBase
{
    /// <summary>Processes an incoming notification.</summary>
    [HttpPost]
    public IActionResult Create(NotificationRequest request)
    {
        return Ok();
    }
}
