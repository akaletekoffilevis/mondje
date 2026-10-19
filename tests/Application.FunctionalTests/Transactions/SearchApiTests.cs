using System.Net;
using System.Net.Http.Json;
using System.Text;
using Mondje.Application.Imports.Common;

namespace Mondje.Application.FunctionalTests.Transactions;

/// <summary>
/// Search by word in the description (challenge 3): the word index must match
/// regardless of case and accents, combine with the other filters, stay in sync with
/// edits and imports, and paginate without duplicates or skips.
/// </summary>
public class SearchApiTests : TestBase
{
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task CreateAsync(
        string id, string description, string type = "CREDIT", long amount = 1000)
    {
        var response = await Client.PostAsync("/api/Transactions", Json($$"""
            {"id":"{{id}}","type":"{{type}}","amount":{{amount}},"description":"{{description}}"}
            """));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task WaitForImportAsync(Guid importId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var dto = await Client.GetFromJsonAsync<ImportDto>($"/api/Imports/{importId}");
            if (dto?.status is "COMPLETED" or "FAILED")
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Import {importId} did not finish in time.");
    }

    [Test]
    public async Task Search_finds_a_word_regardless_of_case_and_accents()
    {
        await CreateAsync("aaaa0000-0000-4000-8000-000000000001", "Paiement facture électricité");
        await CreateAsync("aaaa0000-0000-4000-8000-000000000002", "Vente au marché");
        await CreateAsync("aaaa0000-0000-4000-8000-000000000003", "Achat carburant");

        var accented = await Client.GetFromJsonAsync<ListPage>(
            $"/api/Transactions?q={Uri.EscapeDataString("ÉLECTRICITÉ")}");
        accented!.Items.Length.ShouldBe(1);
        accented.Items[0].Id.ShouldBe("aaaa0000-0000-4000-8000-000000000001");

        var unaccented = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?q=electricite");
        unaccented!.Items.Length.ShouldBe(1);
        unaccented.Items[0].Id.ShouldBe("aaaa0000-0000-4000-8000-000000000001");

        var marche = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?q=marche");
        marche!.Items.Length.ShouldBe(1);
        marche.Items[0].Id.ShouldBe("aaaa0000-0000-4000-8000-000000000002");

        var unknown = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?q=inexistant");
        unknown!.Items.ShouldBeEmpty();
        unknown.HasMore.ShouldBeFalse();
    }

    [Test]
    public async Task Search_combines_with_type_and_amount_filters()
    {
        await CreateAsync("bbbb0000-0000-4000-8000-000000000001", "Facture fournisseur", "CREDIT", 5000);
        await CreateAsync("bbbb0000-0000-4000-8000-000000000002", "Facture fournisseur", "DEBIT", 9000);

        var page = await Client.GetFromJsonAsync<ListPage>(
            "/api/Transactions?q=facture&type=DEBIT&minAmount=8000&maxAmount=10000");

        page!.Items.Length.ShouldBe(1);
        page.Items[0].Id.ShouldBe("bbbb0000-0000-4000-8000-000000000002");
        page.Items[0].Amount.ShouldBe(9000);
    }

    [Test]
    public async Task Updating_the_description_updates_the_search_index()
    {
        const string id = "cccc0000-0000-4000-8000-000000000001";
        await CreateAsync(id, "Ancien libellé");

        var update = await Client.PutAsync($"/api/Transactions/{id}",
            Json("""{"amount":1500,"description":"Nouveau libellé"}"""));
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var oldWord = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?q=ancien");
        oldWord!.Items.ShouldBeEmpty();

        var newWord = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?q=nouveau");
        newWord!.Items.Length.ShouldBe(1);
        newWord.Items[0].Id.ShouldBe(id);
    }

    [Test]
    public async Task Imported_descriptions_are_searchable()
    {
        var content = string.Join('\n',
            "dddd0000-0000-4000-8000-000000000001;CREDIT;1200;Règlement loyer boutique",
            "dddd0000-0000-4000-8000-000000000002;DEBIT;800;Achat ciment");

        var upload = await Client.PostAsync("/api/Imports", new MultipartFormDataContent
        {
            { new StringContent(content, Encoding.UTF8), "file", "search.txt" },
        });
        upload.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var dto = await upload.Content.ReadFromJsonAsync<ImportDto>();
        await WaitForImportAsync(dto!.id);

        var loyer = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?q=loyer");
        loyer!.Items.Length.ShouldBe(1);
        loyer.Items[0].Id.ShouldBe("dddd0000-0000-4000-8000-000000000001");

        var ciment = await Client.GetFromJsonAsync<ListPage>("/api/Transactions?q=ciment&type=DEBIT");
        ciment!.Items.Length.ShouldBe(1);
        ciment.Items[0].Id.ShouldBe("dddd0000-0000-4000-8000-000000000002");
    }

    [Test]
    public async Task Search_pagination_never_skips_nor_duplicates()
    {
        for (var i = 1; i <= 5; i++)
        {
            await CreateAsync($"eeee0000-0000-4000-8000-00000000000{i}", $"Paiement échelonné {i}");
        }

        var seen = new List<string>();
        string? cursor = null;

        do
        {
            var url = "/api/Transactions?q=paiement&limit=2"
                + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

            var page = await Client.GetFromJsonAsync<ListPage>(url);
            page.ShouldNotBeNull();
            seen.AddRange(page!.Items.Select(i => i.Id));
            cursor = page.HasMore ? page.NextCursor : null;
        } while (cursor is not null);

        seen.Count.ShouldBe(5);
        seen.Distinct().Count().ShouldBe(5);
    }

    private record ListPage(Item[] Items, string? NextCursor, bool HasMore, int Limit);

    private record Item(string Id, string Description, string Type, long Amount);
}
