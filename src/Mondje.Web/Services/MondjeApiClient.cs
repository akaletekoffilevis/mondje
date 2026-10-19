using System.Net.Http.Json;
using Mondje.Web.Models;

namespace Mondje.Web.Services;

public class MondjeApiClient(HttpClient httpClient)
{
    public async Task<ApiStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        try { return await httpClient.GetFromJsonAsync<ApiStatus>("/api/Status", ct); }
        catch (HttpRequestException) { return null; }
    }

    public async Task<TransactionListResponse?> ListTransactionsAsync(
        string? type = null, long? minAmount = null, long? maxAmount = null,
        string? q = null, string? cursor = null, int? limit = null,
        CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(type)) qs.Add($"type={Uri.EscapeDataString(type)}");
        if (minAmount is not null) qs.Add($"minAmount={minAmount}");
        if (maxAmount is not null) qs.Add($"maxAmount={maxAmount}");
        if (!string.IsNullOrEmpty(q)) qs.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrEmpty(cursor)) qs.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (limit is not null) qs.Add($"limit={limit}");
        var url = "/api/Transactions" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return await httpClient.GetFromJsonAsync<TransactionListResponse>(url, ct);
    }

    public async Task<TransactionDto?> GetTransactionAsync(string id, CancellationToken ct = default)
        => await httpClient.GetFromJsonAsync<TransactionDto>($"/api/Transactions/{Uri.EscapeDataString(id)}", ct);

    public async Task<(bool ok, string? error)> CreateTransactionAsync(CreateTransactionRequest req, CancellationToken ct = default)
    {
        var r = await httpClient.PostAsJsonAsync("/api/Transactions", req, ct);
        if (r.IsSuccessStatusCode) return (true, null);
        return (false, await r.Content.ReadAsStringAsync(ct));
    }

    public async Task<(bool ok, string? error)> UpdateTransactionAsync(string id, UpdateTransactionRequest req, CancellationToken ct = default)
    {
        var r = await httpClient.PutAsJsonAsync($"/api/Transactions/{Uri.EscapeDataString(id)}", req, ct);
        if (r.IsSuccessStatusCode) return (true, null);
        return (false, await r.Content.ReadAsStringAsync(ct));
    }

    public async Task<bool> DeleteTransactionAsync(string id, CancellationToken ct = default)
    {
        var r = await httpClient.DeleteAsync($"/api/Transactions/{Uri.EscapeDataString(id)}", ct);
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreTransactionAsync(string id, CancellationToken ct = default)
    {
        var r = await httpClient.PostAsync($"/api/Transactions/{Uri.EscapeDataString(id)}/restore", null, ct);
        return r.IsSuccessStatusCode;
    }

    public async Task<BalanceDto?> GetBalanceAsync(DateTime? at = null, CancellationToken ct = default)
    {
        var url = "/api/Transactions/balance";
        if (at is not null) url += $"?at={Uri.EscapeDataString(at.Value.ToString("O"))}";
        return await httpClient.GetFromJsonAsync<BalanceDto>(url, ct);
    }

    public async Task<ImportListResponse?> ListImportsAsync(string? cursor = null, int? limit = null, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(cursor)) qs.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (limit is not null) qs.Add($"limit={limit}");
        var url = "/api/Imports" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return await httpClient.GetFromJsonAsync<ImportListResponse>(url, ct);
    }

    public async Task<ImportDto?> GetImportAsync(Guid id, CancellationToken ct = default)
        => await httpClient.GetFromJsonAsync<ImportDto>($"/api/Imports/{id}", ct);

    public async Task<ImportErrorListResponse?> GetImportErrorsAsync(Guid id, string? cursor = null, int? limit = null, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(cursor)) qs.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (limit is not null) qs.Add($"limit={limit}");
        var url = $"/api/Imports/{id}/errors" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return await httpClient.GetFromJsonAsync<ImportErrorListResponse>(url, ct);
    }

    public async Task<(bool ok, ImportDto? dto, string? error)> UploadImportAsync(Stream fileStream, string fileName, string? agency, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var sc = new StreamContent(fileStream);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(sc, "file", fileName);
        if (!string.IsNullOrEmpty(agency)) content.Add(new StringContent(agency), "agency");
        var r = await httpClient.PostAsync("/api/Imports", content, ct);
        if (r.IsSuccessStatusCode)
        {
            var dto = await r.Content.ReadFromJsonAsync<ImportDto>(ct);
            return (true, dto, null);
        }
        return (false, null, await r.Content.ReadAsStringAsync(ct));
    }

    public async Task<bool> CancelImportAsync(Guid id, CancellationToken ct = default)
    {
        var r = await httpClient.DeleteAsync($"/api/Imports/{id}", ct);
        return r.IsSuccessStatusCode;
    }

    public async Task<HttpResponseMessage> ExportAsync(string format, CancellationToken ct = default)
        => await httpClient.GetAsync($"/api/Transactions/export?format={format}", HttpCompletionOption.ResponseHeadersRead, ct);
}