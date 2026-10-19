using System.Globalization;
using System.Text;
using System.Text.Json;
using Mondje.Application.Transactions.Commands.CreateTransaction;
using Mondje.Application.Transactions.Commands.DeleteTransaction;
using Mondje.Application.Transactions.Commands.RestoreTransaction;
using Mondje.Application.Transactions.Commands.UpdateTransaction;
using Mondje.Application.Transactions.Common;
using Mondje.Application.Transactions.Queries.ExportTransactions;
using Mondje.Application.Transactions.Queries.GetBalance;
using Mondje.Application.Transactions.Queries.GetTransaction;
using Mondje.Application.Transactions.Queries.ListTransactions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mondje.Api.Endpoints;

/// <summary>
/// CRUD endpoints for transactions. Validation failures surface as 422 and missing or
/// conflicting resources as 404/409 through the global exception handler
/// (<see cref="ProblemDetailsExceptionHandler"/>), which is why the handlers below only
/// return the success shapes.
/// </summary>
public class Transactions : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(List);
        groupBuilder.MapGet(Export, "export");
        groupBuilder.MapGet(Balance, "balance");
        groupBuilder.MapGet(Get, "{id}");
        groupBuilder.MapPost(Create);
        groupBuilder.MapPut(Update, "{id}");
        groupBuilder.MapDelete(Delete, "{id}");
        groupBuilder.MapPost(Restore, "{id}/restore");
    }

    [EndpointSummary("List transactions (cursor paginated)")]
    public static async Task<Ok<TransactionListResponse>> List(
        ISender sender, [AsParameters] ListTransactionsQuery query, CancellationToken cancellationToken)
    {
        var result = await sender.Send(query, cancellationToken);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get one transaction")]
    public static async Task<Ok<TransactionDto>> Get(
        ISender sender, string id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTransactionQuery { Id = id }, cancellationToken);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Create a transaction")]
    public static async Task<Created<string>> Create(
        ISender sender, CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        var id = await sender.Send(command, cancellationToken);
        return TypedResults.Created($"/api/Transactions/{id}", id);
    }

    [EndpointSummary("Update amount and description")]
    public static async Task<NoContent> Update(
        ISender sender, string id, UpdateTransactionCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        await sender.Send(command, cancellationToken);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Soft-delete a transaction (restorable 24h)")]
    public static async Task<NoContent> Delete(
        ISender sender, string id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteTransactionCommand { Id = id }, cancellationToken);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Restore a soft-deleted transaction")]
    public static async Task<NoContent> Restore(
        ISender sender, string id, CancellationToken cancellationToken)
    {
        await sender.Send(new RestoreTransactionCommand { Id = id }, cancellationToken);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Export transactions (.json = NDJSON one object per line, or .csv)")]
    public static async Task<IResult> Export(
        ISender sender, [AsParameters] ExportTransactionsQuery query, CancellationToken cancellationToken)
    {
        var format = (query.Format ?? "json").Trim().ToLowerInvariant();
        if (format is not ("json" or "csv"))
        {
            return ApiErrors.Unprocessable(
                "format",
                $"Format d'export inconnu '{query.Format}' : utilisez 'json' (NDJSON, un objet par ligne) ou 'csv'.");
        }

        var lines = await sender.Send(query, cancellationToken);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // Streamed straight to the socket: a 1M-row export never materialises in memory.
        return format == "csv"
            ? TypedResults.Stream(
                s => WriteCsvAsync(s, lines, cancellationToken),
                "text/csv; charset=utf-8",
                $"transactions-{stamp}.csv")
            : TypedResults.Stream(
                s => WriteNdjsonAsync(s, lines, cancellationToken),
                "application/x-ndjson; charset=utf-8",
                $"transactions-{stamp}.json");
    }

    [EndpointSummary("Balance (CREDIT - DEBIT) at a date, without recalculating the table")]
    public static async Task<Ok<BalanceDto>> Balance(
        ISender sender, [AsParameters] GetBalanceQuery query, CancellationToken cancellationToken)
        => TypedResults.Ok(await sender.Send(query, cancellationToken));

    /// <summary>NDJSON: one JSON object per line, same field names as the import contract.</summary>
    private static async Task WriteNdjsonAsync(
        Stream stream,
        IAsyncEnumerable<TransactionExportLine> lines,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        await foreach (var line in lines.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(line, options).AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteCsvAsync(
        Stream stream,
        IAsyncEnumerable<TransactionExportLine> lines,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
        };

        await writer.WriteLineAsync("id,type,montant,description,agency,createdAtUtc".AsMemory(), cancellationToken);

        await foreach (var line in lines.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(string.Join(',',
                Csv(line.id),
                Csv(line.type),
                line.montant,
                Csv(line.description),
                Csv(line.agency ?? string.Empty),
                line.createdAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
            ).AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static string Csv(string value) =>
        value.IndexOfAny(['"', ',', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
