using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mondje.Application.FunctionalTests.Transactions;

/// <summary>
/// HTTP contract of the export (challenge 1: .json = NDJSON) and balance (challenge 11)
/// endpoints, plus the export = balance consistency rule (challenge 14).
/// </summary>
public class ExportBalanceApiTests : TestBase
{
    private static StringContent Json(string body) =>
        new(body, System.Text.Encoding.UTF8, "application/json");

    private static string NewId() => Guid.NewGuid().ToString();

    private static string TransactionJson(
        string id,
        string type = "CREDIT",
        long amount = 15000,
        string description = "Vente du jour")
    {
        // Escape so any description (quotes included) stays valid JSON.
        var safeDescription = description.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $$"""{"id":"{{id}}","type":"{{type}}","amount":{{amount}},"description":"{{safeDescription}}","agency":"Douala"}""";
    }

    private async Task CreateAsync(
        string id,
        string type = "CREDIT",
        long amount = 15000,
        string description = "Vente du jour")
    {
        var response = await Client.PostAsync("/api/Transactions", Json(TransactionJson(id, type, amount, description)));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static (long credit, long debit) TotalsFromNdjson(string content)
    {
        long credit = 0, debit = 0;
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var montant = doc.RootElement.GetProperty("montant").GetInt64();
            if (doc.RootElement.GetProperty("type").GetString() == "CREDIT")
            {
                credit += montant;
            }
            else
            {
                debit += montant;
            }
        }

        return (credit, debit);
    }

    [Test]
    public async Task Export_json_is_ndjson_with_import_field_names()
    {
        await CreateAsync(NewId(), "CREDIT", 15000, "Vente marché");
        await CreateAsync(NewId(), "CREDIT", 25000, "Livraison");
        await CreateAsync(NewId(), "DEBIT", 5000, "Achat papier");

        var response = await Client.GetAsync("/api/Transactions/export?format=json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-ndjson");
        response.Content.Headers.ContentDisposition!.FileName!.ShouldContain(".json");

        var lines = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(3);

        // L'export réutilise les champs de l'import NDJSON : il est réimportable tel quel.
        using var first = JsonDocument.Parse(lines[0]);
        first.RootElement.TryGetProperty("montant", out _).ShouldBeTrue();
        first.RootElement.TryGetProperty("id", out _).ShouldBeTrue();
        first.RootElement.TryGetProperty("description", out _).ShouldBeTrue();

        var (credit, debit) = TotalsFromNdjson(string.Join('\n', lines));
        (credit - debit).ShouldBe(15000 + 25000 - 5000);
    }

    [Test]
    public async Task Export_total_equals_displayed_balance()
    {
        await CreateAsync(NewId(), "CREDIT", 100000);
        await CreateAsync(NewId(), "DEBIT", 40000);
        var deleted = NewId();
        await CreateAsync(deleted, "DEBIT", 9999);
        (await Client.DeleteAsync($"/api/Transactions/{deleted}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var export = await Client.GetStringAsync("/api/Transactions/export?format=json");
        var (credit, debit) = TotalsFromNdjson(export);

        var balance = await Client.GetFromJsonAsync<BalanceResponse>("/api/Transactions/balance");
        balance.ShouldNotBeNull();
        balance.credit.ShouldBe(credit);
        balance.debit.ShouldBe(debit);
        balance.balance.ShouldBe(credit - debit);
    }

    [Test]
    public async Task Export_csv_and_json_contain_the_same_transactions()
    {
        await CreateAsync(NewId(), "CREDIT", 10, "Virgule, guillemet \" et accent é");
        await CreateAsync(NewId(), "DEBIT", 20);

        var json = await Client.GetStringAsync("/api/Transactions/export?format=json");
        var csvResponse = await Client.GetAsync("/api/Transactions/export?format=csv");
        csvResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csv = await csvResponse.Content.ReadAsStringAsync();

        var jsonLines = json.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var csvLines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        csvLines.Length.ShouldBe(jsonLines.Length + 1, "l'en-tête CSV en plus des mêmes transactions");
        csvLines[0].ShouldStartWith("id,type,montant,description");

        var jsonTotal = jsonLines.Sum(l => JsonDocument.Parse(l).RootElement.GetProperty("montant").GetInt64());
        var csvTotal = csvLines.Skip(1)
            .Select(l => long.Parse(l.Split(',')[2], CultureInfo.InvariantCulture))
            .Sum();
        csvTotal.ShouldBe(jsonTotal);
    }

    [Test]
    public async Task Export_respects_type_filter()
    {
        await CreateAsync(NewId(), "CREDIT", 100);
        await CreateAsync(NewId(), "DEBIT", 200);

        var json = await Client.GetStringAsync("/api/Transactions/export?format=json&type=DEBIT");
        var lines = json.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(1);
        JsonDocument.Parse(lines[0]).RootElement.GetProperty("type").GetString().ShouldBe("DEBIT");
    }

    [Test]
    public async Task Export_unknown_format_returns_422_with_reason()
    {
        var response = await Client.GetAsync("/api/Transactions/export?format=xml");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Format d'export inconnu");
    }

    [Test]
    public async Task Balance_returns_sums_and_rejects_bad_date()
    {
        await CreateAsync(NewId(), "CREDIT", 15000);
        await CreateAsync(NewId(), "DEBIT", 3000);

        var balance = await Client.GetFromJsonAsync<BalanceResponse>("/api/Transactions/balance");
        balance.ShouldNotBeNull();
        balance.credit.ShouldBe(15000);
        balance.debit.ShouldBe(3000);
        balance.balance.ShouldBe(12000);
        balance.currency.ShouldBe("FCFA");

        var past = await Client.GetFromJsonAsync<BalanceResponse>("/api/Transactions/balance?at=2000-01-01T00:00:00Z");
        past.ShouldNotBeNull();
        past.balance.ShouldBe(0);
        past.transactionCount.ShouldBe(0);

        var invalid = await Client.GetAsync("/api/Transactions/balance?at=hier");
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadAsStringAsync()).ShouldContain("ISO 8601");
    }

    [Test]
    public async Task Balance_excludes_soft_deleted_rows()
    {
        var id = NewId();
        await CreateAsync(id, "CREDIT", 50000);
        (await Client.DeleteAsync($"/api/Transactions/{id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var balance = await Client.GetFromJsonAsync<BalanceResponse>("/api/Transactions/balance");
        balance.ShouldNotBeNull();
        balance.balance.ShouldBe(0);
        balance.transactionCount.ShouldBe(0);
    }

    private sealed record BalanceResponse(
        DateTime at,
        long credit,
        long debit,
        long balance,
        long transactionCount,
        string currency);
}
