using System.Net;
using System.Net.Http.Json;
using System.Text;
using Mondje.Application.Imports.Common;

namespace Mondje.Application.FunctionalTests.Status;

/// <summary>
/// Challenge 15: the dashboard shows ledger totals, import outcomes (time, processed
/// lines, errors) and the three agencies. It must always agree with the balance and the
/// export, which use the same live-row set.
/// </summary>
public class StatusApiTests : TestBase
{
    private const string Endpoint = "/api/Status";

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    private static async Task CreateAsync(string id, string type, long amount, string agency = "Douala")
    {
        var response = await Client.PostAsync("/api/Transactions", Json($$"""
            {"id":"{{id}}","type":"{{type}}","amount":{{amount}},"description":"Dashboard","agency":"{{agency}}"}
            """));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<ImportDto> UploadAndWaitAsync(string content, string fileName = "dashboard.txt")
    {
        var upload = await Client.PostAsync("/api/Imports", new MultipartFormDataContent
        {
            { new StringContent(content, Encoding.UTF8), "file", fileName },
        });
        upload.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var dto = await upload.Content.ReadFromJsonAsync<ImportDto>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var current = await Client.GetFromJsonAsync<ImportDto>($"/api/Imports/{dto!.id}");
            if (current?.status is "COMPLETED" or "FAILED" or "CANCELLED")
            {
                return current;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Import did not finish in time.");
    }

    [Test]
    public async Task Status_endpoint_is_healthy()
    {
        var status = await Client.GetFromJsonAsync<ApiStatus>(Endpoint);

        status.ShouldNotBeNull();
        status.status.ShouldBe("ok");
        status.utc.ShouldNotBe(default);
    }

    [Test]
    public async Task Empty_ledger_dashboard_exposes_the_three_agencies_with_zeros()
    {
        var dashboard = await Client.GetFromJsonAsync<DashboardDto>($"{Endpoint}/dashboard");

        dashboard.ShouldNotBeNull();
        dashboard.ledger.transactions.ShouldBe(0);
        dashboard.ledger.balance.ShouldBe(0);
        dashboard.imports.total.ShouldBe(0);
        dashboard.agencies.Count.ShouldBe(3);
        dashboard.agencies.ShouldAllBe(a => a.transactions == 0 && a.balance == 0);
        dashboard.generatedAtUtc.ShouldNotBe(default);
    }

    [Test]
    public async Task Dashboard_totals_match_created_transactions()
    {
        await CreateAsync("f0000000-0000-4000-8000-000000000001", "CREDIT", 5000, "Douala");
        await CreateAsync("f0000000-0000-4000-8000-000000000002", "DEBIT", 1200, "Douala");
        await CreateAsync("f0000000-0000-4000-8000-000000000003", "CREDIT", 800, "Bafoussam");

        var dashboard = await Client.GetFromJsonAsync<DashboardDto>($"{Endpoint}/dashboard");

        dashboard.ShouldNotBeNull();
        dashboard.ledger.transactions.ShouldBe(3);
        dashboard.ledger.credits.ShouldBe(2);
        dashboard.ledger.debits.ShouldBe(1);
        dashboard.ledger.creditAmount.ShouldBe(5800);
        dashboard.ledger.debitAmount.ShouldBe(1200);
        dashboard.ledger.balance.ShouldBe(4600);
        dashboard.ledger.manual.ShouldBe(3);
        dashboard.ledger.imported.ShouldBe(0);

        var douala = dashboard.agencies.Single(a => a.agency == "Douala");
        douala.transactions.ShouldBe(2);
        douala.balance.ShouldBe(3800);

        var bafoussam = dashboard.agencies.Single(a => a.agency == "Bafoussam");
        bafoussam.transactions.ShouldBe(1);
        bafoussam.balance.ShouldBe(800);

        var yaounde = dashboard.agencies.Single(a => a.agency == "Yaoundé");
        yaounde.transactions.ShouldBe(0);
    }

    [Test]
    public async Task Dashboard_balance_equals_the_balance_endpoint()
    {
        await CreateAsync("f1000000-0000-4000-8000-000000000001", "CREDIT", 9000);
        await CreateAsync("f1000000-0000-4000-8000-000000000002", "DEBIT", 4500);

        var dashboard = await Client.GetFromJsonAsync<DashboardDto>($"{Endpoint}/dashboard");
        var balance = await Client.GetFromJsonAsync<BalanceDto>("/api/Transactions/balance");

        dashboard.ShouldNotBeNull();
        balance.ShouldNotBeNull();
        dashboard.ledger.balance.ShouldBe(balance.balance);
        dashboard.ledger.transactions.ShouldBe(balance.transactionCount);
    }

    [Test]
    public async Task Dashboard_reports_import_lines_duration_and_errors()
    {
        var done = await UploadAndWaitAsync(string.Join('\n',
            "f2000000-0000-4000-8000-000000000001;CREDIT;1000;Vente",
            "f2000000-0000-4000-8000-000000000002;DEBIT;400;Achat",
            "pas-un-uuid;CREDIT;100;Cassée"));

        done.status.ShouldBe("COMPLETED");
        done.insertedCount.ShouldBe(2);
        done.errorCount.ShouldBe(1);

        var dashboard = await Client.GetFromJsonAsync<DashboardDto>($"{Endpoint}/dashboard");

        dashboard.ShouldNotBeNull();
        dashboard.imports.total.ShouldBe(1);
        dashboard.imports.completed.ShouldBe(1);
        dashboard.imports.totalLines.ShouldBe(3);
        dashboard.imports.processedLines.ShouldBe(3);
        dashboard.imports.inserted.ShouldBe(2);
        dashboard.imports.errors.ShouldBe(1);
        dashboard.imports.lastFileName.ShouldBe("dashboard.txt");
        dashboard.imports.lastDurationMs.ShouldNotBeNull();
        dashboard.imports.lastDurationMs!.Value.ShouldBeGreaterThanOrEqualTo(0);
        dashboard.ledger.imported.ShouldBe(2);
    }

    [Test]
    public async Task Dashboard_excludes_soft_deleted_transactions()
    {
        await CreateAsync("f3000000-0000-4000-8000-000000000001", "CREDIT", 7000);
        (await Client.DeleteAsync("/api/Transactions/f3000000-0000-4000-8000-000000000001"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var dashboard = await Client.GetFromJsonAsync<DashboardDto>($"{Endpoint}/dashboard");

        dashboard.ShouldNotBeNull();
        dashboard.ledger.transactions.ShouldBe(0);
        dashboard.ledger.balance.ShouldBe(0);
        dashboard.ledger.deleted.ShouldBe(1);
    }

    private sealed record ApiStatus(string status, DateTimeOffset utc);

    private sealed record DashboardDto(
        DateTime generatedAtUtc,
        Ledger ledger,
        Imports imports,
        List<Agency> agencies);

    private sealed record Ledger(
        long transactions,
        long credits,
        long debits,
        long creditAmount,
        long debitAmount,
        long balance,
        long deleted,
        long imported,
        long manual);

    private sealed record Imports(
        int total,
        int pending,
        int running,
        int completed,
        int failed,
        int cancelled,
        long totalLines,
        long processedLines,
        long inserted,
        long duplicates,
        long errors,
        long? lastDurationMs,
        DateTime? lastCompletedAtUtc,
        string? lastFileName);

    private sealed record Agency(
        string agency,
        long transactions,
        long creditAmount,
        long debitAmount,
        long balance);

    private sealed record BalanceDto(
        DateTime at,
        long credit,
        long debit,
        long balance,
        long transactionCount,
        string currency);
}
