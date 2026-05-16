using Microsoft.AspNetCore.Mvc;

namespace Todos.Web.Controllers;

/// <summary>Serves the generic error page.</summary>
[Route("/Error")]
public class ErrorController : Controller
{
    /// <summary>Displays the error page.</summary>
    public IActionResult Index() => View();
}
