using Microsoft.AspNetCore.Mvc;
using Mondje.Web.Models;
using Mondje.Web.Services;

namespace Mondje.Web.Controllers;

public class TransactionsController(MondjeApiClient api) : Controller
{
    public async Task<IActionResult> Index(
        string? type, long? minAmount, long? maxAmount, string? q,
        string? cursor, int? limit, CancellationToken ct)
    {
        var page = await api.ListTransactionsAsync(type, minAmount, maxAmount, q, cursor, limit ?? 50, ct);
        ViewBag.Type = type;
        ViewBag.MinAmount = minAmount;
        ViewBag.MaxAmount = maxAmount;
        ViewBag.Q = q;
        return View(page);
    }

    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        var t = await api.GetTransactionAsync(id, ct);
        if (t is null) return NotFound();
        return View(t);
    }

    public IActionResult Create() => View(new CreateTransactionRequest(Guid.NewGuid().ToString(), "CREDIT", 1000, "Description", null));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTransactionRequest req, CancellationToken ct)
    {
        var (ok, err) = await api.CreateTransactionAsync(req, ct);
        if (!ok) { ModelState.AddModelError("", err ?? "Erreur"); return View(req); }
        TempData["Msg"] = "Transaction créée.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var t = await api.GetTransactionAsync(id, ct);
        if (t is null) return NotFound();
        return View(new EditVm(id, t.amount, t.description));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, EditVm vm, CancellationToken ct)
    {
        if (vm.Amount <= 0) ModelState.AddModelError("Amount", "Montant strictement positif.");
        if (string.IsNullOrWhiteSpace(vm.Description) || vm.Description.Length > 255) ModelState.AddModelError("Description", "Description 1..255 caractères.");
        if (!ModelState.IsValid) return View(vm);
        var (ok, err) = await api.UpdateTransactionAsync(id, new UpdateTransactionRequest(vm.Amount, vm.Description), ct);
        if (!ok) { ModelState.AddModelError("", err ?? "Erreur"); return View(vm); }
        TempData["Msg"] = "Transaction mise à jour.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await api.DeleteTransactionAsync(id, ct);
        TempData["Msg"] = "Transaction supprimée (restaurable 24h).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(string id, CancellationToken ct)
    {
        await api.RestoreTransactionAsync(id, ct);
        TempData["Msg"] = "Transaction restaurée.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Balance(DateTime? at, CancellationToken ct)
    {
        var b = await api.GetBalanceAsync(at, ct);
        ViewBag.At = at;
        return View(b);
    }
}

public record EditVm(string Id, long Amount, string Description);