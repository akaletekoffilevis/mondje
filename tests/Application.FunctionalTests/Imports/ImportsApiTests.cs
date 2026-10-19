using System.Net;
using System.Net.Http.Json;
using System.Text;
using Mondje.Application.Imports.Common;

namespace Mondje.Application.FunctionalTests.Imports;

/// <summary>
/// HTTP contract tests for the Import API: 202 upload, background processing counters,
/// first-wins deduplication, broken-line report (paginated), 422 refusals and cancellation.
/// </summary>
public class ImportsApiTests : TestBase
{
    private const string Endpoint = "/api/Imports";

    private static MultipartFormDataContent Upload(string content, string fileName, string? agency = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(content, Encoding.UTF8), "file", fileName },
        };
        if (agency is not null)
        {
            form.Add(new StringContent(agency), "agency");
        }

        return form;
    }

    private static async Task<ImportDto> WaitAsync(Guid importId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (DateTime.UtcNow < deadline)
        {
            var dto = await Client.GetFromJsonAsync<ImportDto>($"{Endpoint}/{importId}");
            if (dto is not null && dto.status is "COMPLETED" or "FAILED" or "CANCELLED")
            {
                return dto;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Import {importId} did not finish in time.");
    }

    private static string ValidLine(int index, string type = "CREDIT") =>
        $"{index:D8}-0000-4000-8000-{index:D12};{type};{1000 + index};Vente numero {index}";

    [Test]
    public async Task Upload_txt_returns_202_and_processes_valid_and_broken_lines()
    {
        var content = string.Join('\n',
            ValidLine(1),
            ValidLine(2, "DEBIT"),
            ValidLine(3),
            "pas-un-uuid;CREDIT;100;Description",
            "00000004-0000-4000-8000-000000000004;VIREMENT;100;Type inconnu");

        var response = await Client.PostAsync(Endpoint, Upload(content, "lignes.txt"));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<ImportDto>();
        created.ShouldNotBeNull();
        created!.status.ShouldBe("PENDING");

        var done = await WaitAsync(created.id);
        done.status.ShouldBe("COMPLETED");
        done.totalLines.ShouldBe(5);
        done.insertedCount.ShouldBe(3);
        done.errorCount.ShouldBe(2);

        // Broken lines are isolated and reported with their line number and reason.
        var errors = await Client.GetFromJsonAsync<ImportErrorListResponse>(
            $"{Endpoint}/{created.id}/errors");
        errors.ShouldNotBeNull();
        errors!.items.Count.ShouldBe(2);
        errors.items[0].lineNumber.ShouldBe(4);
        errors.items[0].reason.ShouldContain("UUID valide");
        errors.items[1].lineNumber.ShouldBe(5);
        errors.items[1].reason.ShouldContain("CREDIT ou DEBIT");
    }

    [Test]
    public async Task Reuploading_the_same_file_never_double_counts()
    {
        var content = string.Join('\n', ValidLine(10), ValidLine(11), ValidLine(12));

        var first = await Client.PostAsync(Endpoint, Upload(content, "f.txt"));
        var firstDto = await first.Content.ReadFromJsonAsync<ImportDto>();
        var firstDone = await WaitAsync(firstDto!.id);
        firstDone.insertedCount.ShouldBe(3);

        var second = await Client.PostAsync(Endpoint, Upload(content, "f.txt"));
        var secondDto = await second.Content.ReadFromJsonAsync<ImportDto>();
        var secondDone = await WaitAsync(secondDto!.id);

        secondDone.status.ShouldBe("COMPLETED");
        secondDone.insertedCount.ShouldBe(0);
        secondDone.duplicateCount.ShouldBe(3);
        secondDone.errorCount.ShouldBe(0);
    }

    [Test]
    public async Task Single_json_array_is_refused_with_explicit_message()
    {
        var response = await Client.PostAsync(Endpoint,
            Upload("""[{"id":"00000001-0000-4000-8000-000000000099","type":"CREDIT","montant":100}]""", "array.ndjson"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("tableau JSON");
        body.ShouldContain("objet JSON indépendant");
    }

    [Test]
    public async Task Upload_without_file_returns_422()
    {
        // A multipart form without the file field (but with another field) must be
        // refused with an explicit reason instead of crashing on a null file.
        var form = new MultipartFormDataContent
        {
            { new StringContent("Douala"), "agency" },
        };
        var response = await Client.PostAsync(Endpoint, form);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("fichier non vide");
    }

    [Test]
    public async Task Ndjson_lines_are_parsed_and_invalid_amount_is_reported()
    {
        var content = string.Join('\n',
            """{"id":"00000001-0000-4000-8000-000000000101","type":"DEBIT","montant":250,"description":"Achat fournitures"}""",
            """{"id":"00000002-0000-4000-8000-000000000102","type":"CREDIT","montant":15.5,"description":"Montant flottant"}""",
            "{pas du json}");

        var response = await Client.PostAsync(Endpoint, Upload(content, "lignes.ndjson", agency: "Douala"));
        var dto = await response.Content.ReadFromJsonAsync<ImportDto>();
        var done = await WaitAsync(dto!.id);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        done.format.ShouldBe("NDJSON");
        done.agency.ShouldBe("Douala");
        done.totalLines.ShouldBe(3);
        done.insertedCount.ShouldBe(1);
        done.errorCount.ShouldBe(2);

        var errors = await Client.GetFromJsonAsync<ImportErrorListResponse>(
            $"{Endpoint}/{dto.id}/errors");
        errors!.items.Count(e => e.reason.Contains("montant")).ShouldBe(1);
        errors.items.Count(e => e.reason.Contains("JSON")).ShouldBe(1);
    }

    [Test]
    public async Task Unknown_import_returns_404()
    {
        var response = await Client.GetAsync($"{Endpoint}/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Import_list_is_paginated()
    {
        for (var i = 0; i < 3; i++)
        {
            var uploaded = await Client.PostAsync(Endpoint, Upload(ValidLine(i), $"p{i}.txt"));
            var dto = await uploaded.Content.ReadFromJsonAsync<ImportDto>();
            await WaitAsync(dto!.id);
        }

        var page = await Client.GetFromJsonAsync<ImportListResponse>($"{Endpoint}?limit=2");
        page.ShouldNotBeNull();
        page!.items.Count.ShouldBe(2);
        page.hasMore.ShouldBeTrue();
        page.nextCursor.ShouldNotBeNull();

        var nextPage = await Client.GetFromJsonAsync<ImportListResponse>(
            $"{Endpoint}?limit=2&cursor={Uri.EscapeDataString(page.nextCursor!)}");
        nextPage!.items.Count.ShouldBe(1);
        nextPage.hasMore.ShouldBeFalse();

        // Cursors must not repeat or skip batches.
        page.items.Select(i => i.id).Intersect(nextPage.items.Select(i => i.id)).ShouldBeEmpty();
    }

    [Test]
    public async Task Cancel_import_deletes_only_its_own_untouched_transactions()
    {
        // A transaction created manually (not part of any import) must survive.
        var manual = await Client.PostAsync("/api/Transactions",
            new StringContent("""{"id":"aabbccdd-0000-4000-8000-000000000090","type":"CREDIT","amount":5000,"description":"Transaction manuelle"}""",
                Encoding.UTF8, "application/json"));
        manual.StatusCode.ShouldBe(HttpStatusCode.Created);

        var uploaded = await Client.PostAsync(Endpoint, Upload(string.Join('\n', ValidLine(20), ValidLine(21)), "cancel.txt"));
        var dto = await uploaded.Content.ReadFromJsonAsync<ImportDto>();
        await WaitAsync(dto!.id);

        var cancel = await Client.DeleteAsync($"{Endpoint}/{dto.id}");
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var status = await Client.GetFromJsonAsync<ImportDto>($"{Endpoint}/{dto.id}");
        status!.status.ShouldBe("CANCELLED");

        // The two imported rows are gone, the manual one remains.
        var list = await Client.GetFromJsonAsync<Todo>(
            "/api/Transactions?limit=100");
        list!.items.Count.ShouldBe(1);
        list.items[0].id.ShouldBe("aabbccdd-0000-4000-8000-000000000090");
    }

    private record Todo(IReadOnlyList<IdOnly> items);

    private record IdOnly(string id);
}
