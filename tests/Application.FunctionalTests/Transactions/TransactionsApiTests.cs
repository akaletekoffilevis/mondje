using System.Net;
using System.Net.Http.Json;

namespace Mondje.Application.FunctionalTests.Transactions;

/// <summary>
/// HTTP contract tests for the Transactions API: status codes (201/204/404/409/422),
/// UUID case normalisation, cursor pagination and soft delete/restore.
/// </summary>
public class TransactionsApiTests : TestBase
{
    private static StringContent Json(string body) =>
        new(body, System.Text.Encoding.UTF8, "application/json");

    private static string TransactionJson(
        string id,
        string type = "CREDIT",
        object? amount = null,
        string description = "Transaction de test",
        string? agency = "Douala")
    {
        amount ??= 15000;
        var amountJson = amount is string s ? $"\"{s}\"" : amount.ToString();
        var agencyJson = agency is null ? "null" : $"\"{agency}\"";
        return $$"""{"id":"{{id}}","type":"{{type}}","amount":{{amountJson}},"description":"{{description}}","agency":{{agencyJson}}}""";
    }

    [Test]
    public async Task Create_returns_201_and_normalises_uuid_to_lowercase()
    {
        var response = await Client.PostAsync("/api/Transactions",
            Json(TransactionJson("AABBCCDD-0000-4000-8000-000000000001")));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync())
            .ShouldContain("aabbccdd-0000-4000-8000-000000000001");
    }

    [Test]
    public async Task Create_duplicate_uuid_returns_409()
    {
        const string id = "aabbccdd-0000-4000-8000-000000000002";
        (await Client.PostAsync("/api/Transactions", Json(TransactionJson(id))))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var duplicate = await Client.PostAsync("/api/Transactions",
            Json(TransactionJson(id, type: "DEBIT", amount: 100)));

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).ShouldContain("existe déjà");
    }

    [Test]
    public async Task Create_float_amount_returns_422_with_reason()
    {
        var response = await Client.PostAsync("/api/Transactions",
            Json(TransactionJson("aabbccdd-0000-4000-8000-000000000003", amount: 15.5)));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("entier strictement positif");
    }

    [Test]
    public async Task Create_unknown_type_returns_422()
    {
        var response = await Client.PostAsync("/api/Transactions",
            Json(TransactionJson("aabbccdd-0000-4000-8000-000000000004", type: "VIREMENT")));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("CREDIT ou DEBIT");
    }

    [Test]
    public async Task Create_malformed_uuid_returns_422()
    {
        var response = await Client.PostAsync("/api/Transactions",
            Json(TransactionJson("pas-un-uuid")));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("UUID valide");
    }

    [Test]
    public async Task List_pagination_never_skips_nor_duplicates_across_pages()
    {
        for (var i = 0; i < 5; i++)
        {
            (await Client.PostAsync("/api/Transactions",
                Json(TransactionJson($"aabbccdd-0000-4000-8000-00000000010{i}"))))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var seen = new List<string>();
        string? cursor = null;

        for (var page = 0; page < 10; page++)
        {
            var url = "/api/Transactions?limit=2" + (cursor is null ? "" : $"&cursor={cursor}");
            var body = await Client.GetFromJsonAsync<ListPage>(url);
            body.ShouldNotBeNull();
            seen.AddRange(body.Items.Select(t => t.Id));
            if (!body.HasMore) break;
            cursor = body.NextCursor;
            cursor.ShouldNotBeNull();
        }

        seen.Count.ShouldBe(5);
        seen.Distinct().Count().ShouldBe(5);
    }

    [Test]
    public async Task Update_changes_amount_and_description_only_and_marks_edited()
    {
        const string id = "aabbccdd-0000-4000-8000-000000000005";
        (await Client.PostAsync("/api/Transactions",
            Json(TransactionJson(id, type: "DEBIT", amount: 1000, description: "Avant"))))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var update = await Client.PutAsync($"/api/Transactions/{id}",
            Json("""{"amount":7777,"description":"Après"}"""));
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await Client.GetFromJsonAsync<TransactionPayload>($"/api/Transactions/{id}");
        after.ShouldNotBeNull();
        after.Amount.ShouldBe(7777);
        after.Description.ShouldBe("Après");
        after.Type.ShouldBe("DEBIT");      // untouched
        after.EditedAtUtc.ShouldNotBeNull(); // provenance of a manual edit
    }

    [Test]
    public async Task Delete_soft_then_restore_roundtrip()
    {
        const string id = "aabbccdd-0000-4000-8000-000000000006";
        (await Client.PostAsync("/api/Transactions", Json(TransactionJson(id))))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await Client.DeleteAsync($"/api/Transactions/{id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Soft-deleted rows are hidden from the default list...
        var defaultList = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?limit=50");
        defaultList!.Items.ShouldNotContain(t => t.Id == id);

        // ...but visible with includeDeleted so the UI can offer a restore.
        var withDeleted = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?limit=50&includeDeleted=true");
        withDeleted!.Items.ShouldContain(t => t.Id == id);

        (await Client.PostAsync($"/api/Transactions/{id}/restore", null!))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var restoredList = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?limit=50");
        restoredList!.Items.ShouldContain(t => t.Id == id);
    }

    [Test]
    public async Task Get_unknown_uuid_returns_404_without_technical_trace()
    {
        var response = await Client.GetAsync("/api/Transactions/ffffffff-0000-4000-8000-000000000000");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("introuvable");
        body.ShouldNotContain("   at "); // no stack trace leaked
    }

    private record ListPage(TransactionPayload[] Items, string? NextCursor, bool HasMore, int Limit);

    private record TransactionPayload(
        string Id, string Type, long Amount, string Description,
        DateTime CreatedAtUtc, string? Agency, Guid? ImportId, int? SourceLine,
        DateTime? EditedAtUtc, DateTime? DeletedAtUtc);
}
