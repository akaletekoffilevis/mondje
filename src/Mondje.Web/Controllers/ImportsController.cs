using Microsoft.AspNetCore.Mvc;
using Mondje.Web.Models;
using Mondje.Web.Services;

namespace Mondje.Web.Controllers;

public class ImportsController(MondjeApiClient api) : Controller
{
    public async Task<IActionResult> Index(string? cursor, int? limit, CancellationToken ct)
    {
        var page = await api.ListImportsAsync(cursor, limit ?? 20, ct);
        return View(page);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var imp = await api.GetImportAsync(id, ct);
        if (imp is null) return NotFound();
        return View(imp);
    }

    public async Task<IActionResult> Errors(Guid id, string? cursor, CancellationToken ct)
    {
        var imp = await api.GetImportAsync(id, ct);
        if (imp is null) return NotFound();
        var errs = await api.GetImportErrorsAsync(id, cursor, 200, ct);
        ViewBag.Import = imp;
        return View(errs);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, string? agency, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Err"] = "Fichier requis.";
            return RedirectToAction(nameof(Index));
        }
        using var s = file.OpenReadStream();
        var (ok, dto, err) = await api.UploadImportAsync(s, file.FileName, agency, ct);
        if (!ok || dto is null)
        {
            TempData["Err"] = err ?? "Échec de l'upload.";
            return RedirectToAction(nameof(Index));
        }
        TempData["Msg"] = $"Import « {dto.fileName} » accepté ({dto.format}), {dto.totalLines} lignes.";
        return RedirectToAction(nameof(Details), new { id = dto.id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await api.CancelImportAsync(id, ct);
        TempData["Msg"] = "Annulation demandée.";
        return RedirectToAction(nameof(Details), new { id });
    }
}