using System.Net;
using System.Net.Http.Json;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Imports.Commands.ProcessImport;
using Mondje.Application.Imports.Common;
using Mondje.Domain.Entities;
using Mondje.Infrastructure.Imports;

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

    [Test]
    public async Task Two_identical_files_uploaded_simultaneously_converge_to_one_ledger()
    {
        // Challenge 8: the same file sent twice at the same time must end with an
        // identical total and coherent reports on both sides — never double-counted.
        var content = string.Join('\n', Enumerable.Range(1, 2000).Select(i => ValidLine(i)));

        var responses = await Task.WhenAll(
            Task.Run(() => Client.PostAsync(Endpoint, Upload(content, "same.txt"))),
            Task.Run(() => Client.PostAsync(Endpoint, Upload(content, "same.txt"))));

        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            var dto = await response.Content.ReadFromJsonAsync<ImportDto>();
            ids.Add(dto!.id);
        }

        var batches = new[] { await WaitAsync(ids[0]), await WaitAsync(ids[1]) };
        foreach (var done in batches)
        {
            done.status.ShouldBe("COMPLETED");
            done.totalLines.ShouldBe(2000);
            done.errorCount.ShouldBe(0);
            // Every line is accounted for: written by exactly one of the two batches,
            // skipped as duplicate by the other.
            (done.insertedCount + done.duplicateCount).ShouldBe(2000);
        }

        (batches[0].insertedCount + batches[1].insertedCount).ShouldBe(2000);

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var rows = await db.Transactions
            .CountAsync(t => t.ImportId == ids[0] || t.ImportId == ids[1]);
        rows.ShouldBe(2000);
    }

    [Test]
    public async Task Interrupted_import_resumes_at_saved_offset_without_rereading_the_file()
    {
        // Challenge 12: simulate the exact state left by a server killed mid-import —
        // the batch row is RUNNING with a durable byte offset, and the lines before that
        // offset are already in the ledger. The resume must process only the tail.
        const int total = 40;
        const int alreadyDone = 25;

        var lines = Enumerable.Range(1, total).Select(i => ValidLine(i)).ToList();
        var content = string.Join('\n', lines);
        var processedPrefix = string.Join('\n', lines.Take(alreadyDone)) + "\n";
        var batchId = Guid.Parse("0000000a-0000-4000-8000-0000000000aa");

        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var storedFileName = $"{batchId:N}.txt";
            var folder = ImportStorage.GetFolder(config);
            await File.WriteAllTextAsync(
                Path.Combine(folder, storedFileName), content, new UTF8Encoding(false));

            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.ImportBatches.Add(new ImportBatch
            {
                Id = batchId,
                FileName = "coupure.txt",
                StoredFileName = storedFileName,
                FileSizeBytes = Encoding.UTF8.GetByteCount(content),
                Format = ImportBatch.FormatTxt,
                Status = ImportBatch.Running,
                TotalLines = total,
                InsertedCount = alreadyDone,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                ProcessedLines = alreadyDone,
                BytesProcessed = Encoding.UTF8.GetByteCount(processedPrefix),
            });

            for (var i = 1; i <= alreadyDone; i++)
            {
                db.Transactions.Add(new Transaction
                {
                    Id = ValidLine(i).Split(';')[0],
                    Type = Transaction.Credit,
                    Amount = 1000 + i,
                    Description = $"Vente numero {i}",
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    ImportId = batchId,
                    SourceLine = i,
                });
            }

            await db.SaveChangesAsync(CancellationToken.None);

            // The restart: the worker picks the RUNNING batch up and resumes it.
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ProcessImportCommand { ImportId = batchId });
        }

        var done = await WaitAsync(batchId);
        done.status.ShouldBe("COMPLETED");
        done.totalLines.ShouldBe(total);
        done.processedLines.ShouldBe(total);
        done.bytesProcessed.ShouldBe(done.fileSizeBytes);

        // Proof of a true resume: no line of the head was re-read (zero duplicates),
        // the tail was inserted, and the numbering continues where the crash stopped.
        done.duplicateCount.ShouldBe(0);
        done.insertedCount.ShouldBe(total);

        using (var check = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var rows = await db.Transactions
                .Where(t => t.ImportId == batchId)
                .ToListAsync();

            rows.Count.ShouldBe(total);
            rows.Min(t => t.SourceLine).ShouldBe(1);
            rows.Max(t => t.SourceLine).ShouldBe(total);
            // No line number is processed twice — the head was not re-read.
            rows.Select(t => t.SourceLine).Distinct().Count().ShouldBe(total);
            // The tail rows continue the numbering where the crash stopped: 26..40.
            rows.Count(t => t.SourceLine > alreadyDone).ShouldBe(total - alreadyDone);
        }
    }

    [Test]
    public async Task Completed_import_exposes_progress_bytes_and_duration()
    {
        // Challenge 2 needs the real duration, challenge 6 the progress: both are part
        // of the import report.
        var content = string.Join('\n', ValidLine(31), ValidLine(32));
        var response = await Client.PostAsync(Endpoint, Upload(content, "chrono.txt"));
        var dto = await response.Content.ReadFromJsonAsync<ImportDto>();

        var done = await WaitAsync(dto!.id);
        done.status.ShouldBe("COMPLETED");
        done.totalLines.ShouldBe(2);
        done.progressPercent.ShouldBe(100);
        done.processedLines.ShouldBe(done.totalLines);
        done.bytesProcessed.ShouldBe(done.fileSizeBytes);
        done.durationMs.HasValue.ShouldBeTrue();
        done.durationMs!.Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Upload_with_unknown_agency_returns_422()
    {
        var form = Upload(ValidLine(70), "agence.txt", agency: "Kribi");

        var response = await Client.PostAsync(Endpoint, form);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("agence");
    }

    [Test]
    public async Task Cancelling_a_queued_import_never_inserts_anything()
    {
        // The batch exists but no worker has picked it up yet: the user cancels first.
        // Processing that batch afterwards must be a no-op, not a full import.
        var batchId = Guid.NewGuid();
        var content = string.Join('\n', ValidLine(50), ValidLine(51));

        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var storedFileName = $"{batchId:N}.txt";
            await File.WriteAllTextAsync(
                Path.Combine(ImportStorage.GetFolder(config), storedFileName),
                content,
                new UTF8Encoding(false));

            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.ImportBatches.Add(new ImportBatch
            {
                Id = batchId,
                FileName = "en-attente.txt",
                StoredFileName = storedFileName,
                FileSizeBytes = Encoding.UTF8.GetByteCount(content),
                Format = ImportBatch.FormatTxt,
                Status = ImportBatch.Pending,
                TotalLines = 2,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }

        (await Client.DeleteAsync($"{Endpoint}/{batchId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ProcessImportCommand { ImportId = batchId });
        }

        var dto = await Client.GetFromJsonAsync<ImportDto>($"{Endpoint}/{batchId}");
        dto!.status.ShouldBe("CANCELLED");
        dto.insertedCount.ShouldBe(0);

        using (var check = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            (await db.Transactions.CountAsync(t => t.ImportId == batchId)).ShouldBe(0);
        }
    }

    [Test]
    public async Task Cancelling_an_import_keeps_the_rows_edited_by_hand()
    {
        // Challenge 9: surgical cancellation. The untouched imported rows disappear,
        // the one corrected by hand stays in the ledger.
        var uploaded = await Client.PostAsync(Endpoint,
            Upload(string.Join('\n', ValidLine(60), ValidLine(61)), "chirurgical.txt"));
        var dto = await uploaded.Content.ReadFromJsonAsync<ImportDto>();
        await WaitAsync(dto!.id);

        var editedId = ValidLine(60).Split(';')[0];
        var untouchedId = ValidLine(61).Split(';')[0];

        var update = await Client.PutAsync($"/api/Transactions/{editedId}",
            new StringContent("""{"amount":9999,"description":"Corrigé à la main"}""",
                Encoding.UTF8, "application/json"));
        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.DeleteAsync($"{Endpoint}/{dto.id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.GetAsync($"/api/Transactions/{editedId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.GetAsync($"/api/Transactions/{untouchedId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var remaining = await Client.GetFromJsonAsync<SourcePage>("/api/Transactions?limit=100");
        remaining!.items.Count.ShouldBe(1);
        remaining.items[0].id.ShouldBe(editedId);
    }

    [Test]
    public async Task Every_imported_transaction_keeps_its_source_file_and_line()
    {
        var content = string.Join('\n', ValidLine(80), ValidLine(81));
        var uploaded = await Client.PostAsync(Endpoint, Upload(content, "origine.txt", agency: "Yaoundé"));
        var dto = await uploaded.Content.ReadFromJsonAsync<ImportDto>();
        await WaitAsync(dto!.id);

        var page = await Client.GetFromJsonAsync<SourcePage>("/api/Transactions?limit=10");

        page.ShouldNotBeNull();
        page!.items.Count.ShouldBe(2);
        page.items.ShouldAllBe(i => i.sourceFileName == "origine.txt");
        page.items.ShouldAllBe(i => i.importId == dto.id);
        page.items.Select(i => i.sourceLine).OrderBy(n => n).ShouldBe(new int?[] { 1, 2 });
    }

    [Test]
    public async Task Uppercase_uuid_from_a_file_is_stored_lowercase()
    {
        var content = "ABCDEF00-0000-4000-8000-000000000001;CREDIT;1500;Casse haute";

        var uploaded = await Client.PostAsync(Endpoint, Upload(content, "majuscules.txt"));
        var dto = await uploaded.Content.ReadFromJsonAsync<ImportDto>();
        var done = await WaitAsync(dto!.id);

        done.insertedCount.ShouldBe(1);
        done.errorCount.ShouldBe(0);

        var response = await Client.GetAsync(
            "/api/Transactions/abcdef00-0000-4000-8000-000000000001");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private record Todo(IReadOnlyList<IdOnly> items);

    private record IdOnly(string id);

    private record SourcePage(IReadOnlyList<SourceItem> items);

    private record SourceItem(
        string id,
        Guid? importId,
        int? sourceLine,
        string? sourceFileName);
}
