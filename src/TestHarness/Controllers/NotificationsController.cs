using Microsoft.AspNetCore.Mvc;
using Todos.TestHarness.Models;

namespace Todos.TestHarness.Controllers;

[ApiController]
[Route("notifications")]
public class NotificationsController : ControllerBase
{
    private readonly HarnessState _state;

    public NotificationsController(HarnessState state) => _state = state;

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        switch (_state.Mode)
        {
            case "delay":
                await Task.Delay(TimeSpan.FromSeconds(_state.DelaySeconds), cancellationToken);
                return Ok();

            case "error":
                return StatusCode(500);

            case "hang":
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return Ok();

            default: // "ok"
                return Ok();
        }
    }
}
