using Microsoft.AspNetCore.Mvc;

namespace JiraReporter.Api.Controllers;

/// <summary>Serves the dashboard user interface.</summary>
public sealed class DashboardController : Controller
{
    /// <summary>Displays the dashboard.</summary>
    /// <returns>The dashboard view.</returns>
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
