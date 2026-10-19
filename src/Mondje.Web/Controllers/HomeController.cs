using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Mondje.Web.Models;
using Mondje.Web.Services;

namespace Mondje.Web.Controllers;

public class HomeController(MondjeApiClient apiClient) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var status = await apiClient.GetStatusAsync(cancellationToken);
        return View(status);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
