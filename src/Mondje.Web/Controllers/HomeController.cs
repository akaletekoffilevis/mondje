using Microsoft.AspNetCore.Mvc;
using Mondje.Web.Models;
using Mondje.Web.Services;

namespace Mondje.Web.Controllers;

public class HomeController(MondjeApiClient api) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var status = await api.GetStatusAsync(ct);
        var dashboard = await api.GetDashboardAsync(ct);
        ViewBag.Status = status;
        ViewBag.Dashboard = dashboard;
        return View();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
}