using Microsoft.AspNetCore.Mvc;
using Mondje.Web.Models;
using Mondje.Web.Services;

namespace Mondje.Web.Controllers;

public class HomeController(MondjeApiClient api) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var status = await api.GetStatusAsync(ct);
        ViewBag.Status = status;
        return View();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
}