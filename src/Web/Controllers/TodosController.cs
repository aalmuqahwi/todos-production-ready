using Microsoft.AspNetCore.Mvc;

using Todos.Web.Models;

namespace Todos.Web.Controllers;

/// <summary>Manages todo items and notifies the Notifications service on creation.</summary>
public class TodosController : Controller
{
    private static readonly List<Todo> _todos = [];
    private static int _nextId = 1;

    private readonly IHttpClientFactory _httpClientFactory;

    public TodosController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Lists all todos.</summary>
    [HttpGet("/")]
    public IActionResult Index()
    {
        return View(_todos);
    }

    /// <summary>Creates a todo and notifies the Notifications service.</summary>
    [HttpPost("/todos")]
    public async Task<IActionResult> Create(string title, CancellationToken cancellationToken)
    {
        Todo todo = new(_nextId++, title);
        _todos.Add(todo);

        HttpClient client = _httpClientFactory.CreateClient("Notifications");

        try
        {
            await client.PostAsJsonAsync(
                "/notifications",
                new { TodoId = todo.Id, Message = $"Todo '{todo.Title}' was created." },
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(
                detail: "The Notifications service did not respond in time.",
                title: "Gateway Timeout",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }

        return RedirectToAction(nameof(Index));
    }
}
