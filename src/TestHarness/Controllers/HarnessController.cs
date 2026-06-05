using Microsoft.AspNetCore.Mvc;
using Todos.TestHarness.Models;

namespace Todos.TestHarness.Controllers;

[ApiController]
[Route("harness")]
public class HarnessController : ControllerBase
{
    private readonly HarnessState _state;

    public HarnessController(HarnessState state) => _state = state;

    [HttpPost("behavior")]
    public IActionResult SetBehavior(BehaviorRequest request)
    {
        _state.Mode = request.Mode;
        _state.DelaySeconds = request.DelaySeconds;
        return Ok(new { _state.Mode, _state.DelaySeconds });
    }

    [HttpGet("behavior")]
    public IActionResult GetBehavior() =>
        Ok(new { _state.Mode, _state.DelaySeconds });
}
