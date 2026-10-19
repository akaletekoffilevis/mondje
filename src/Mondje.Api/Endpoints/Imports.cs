using Mondje.Application.Imports.Commands.CancelImport;
using Mondje.Application.Imports.Commands.CreateImport;
using Mondje.Application.Imports.Commands.ProcessImport;
using Mondje.Application.Imports.Common;
using Mondje.Application.Imports.Queries.GetImport;
using Mondje.Application.Imports.Queries.ListImportErrors;
using Mondje.Application.Imports.Queries.ListImports;
using Mondje.Domain.Entities;
using Mondje.Infrastructure.Imports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Mondje.Api.Endpoints;

public class Imports : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(UploadImport).DisableAntiforgery();
        groupBuilder.MapGet(ListImports);
        groupBuilder.MapGet(GetImport, "{id}");
        groupBuilder.MapGet(GetImportErrors, "{id}/errors");
        groupBuilder.MapDelete(CancelImport, "{id}");
    }

    [EndpointSummary("Upload and import a TXT or NDJSON file (async, 202 Accepted)")]
    public static async Task<Results<Accepted<ImportDto>, ProblemHttpResult>> UploadImport(
        ISender sender,
        ImportQueue queue,
        IConfiguration configuration,
        IFormFile? file,
        [FromForm] string? agency,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            // Nullable on purpose: a non-nullable IFormFile makes the framework throw
            // BadHttpRequestException before this code runs (a 500 with no explanation).
            return ApiErrors.Unprocessable("file", "Un fichier non vide est requis (champ 'file').");
        }

        // Detect the format from the first non-empty line.
        string? firstLine;
        using (var peek = file.OpenReadStream())
        {
            firstLine = await ReadFirstNonEmptyLineAsync(peek);
        }

        if (firstLine is not null && firstLine.TrimStart().StartsWith('['))
        {
            return ApiErrors.Unprocessable("file",
                "Un fichier NDJSON ne doit pas contenir de tableau JSON : chaque ligne doit être un objet JSON indépendant.");
        }

        var format = firstLine is not null && firstLine.TrimStart().StartsWith('{')
            ? ImportBatch.FormatNdjson
            : ImportBatch.FormatTxt;

        var batchId = Guid.NewGuid();
        var ext = format == ImportBatch.FormatNdjson ? ".ndjson" : ".txt";
        var storedFileName = $"{batchId}{ext}";
        var folder = ImportStorage.GetFolder(configuration);
        var path = Path.Combine(folder, storedFileName);

        await using (var fs = File.Create(path))
        {
            await file.CopyToAsync(fs, cancellationToken);
        }

        var importId = await sender.Send(new CreateImportCommand
        {
            Id = batchId,
            FileName = file.FileName,
            StoredFileName = storedFileName,
            FileSizeBytes = file.Length,
            Format = format,
            Agency = agency,
            RequestId = http.TraceIdentifier,
        }, cancellationToken);

        queue.Enqueue(importId);

        var dto = await sender.Send(new GetImportQuery { Id = importId }, cancellationToken);
        return TypedResults.Accepted($"/api/Imports/{importId}", dto);
    }

    [EndpointSummary("List imports (paginated)")]
    public static async Task<Ok<ImportListResponse>> ListImports(
        ISender sender, [AsParameters] ListImportsQuery query, CancellationToken cancellationToken)
    {
        var result = await sender.Send(query, cancellationToken);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get one import status")]
    public static async Task<Ok<ImportDto>> GetImport(
        ISender sender, Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetImportQuery { Id = id }, cancellationToken);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("List rejected lines of an import (paginated)")]
    public static async Task<Ok<ImportErrorListResponse>> GetImportErrors(
        ISender sender, Guid id, int? limit, int? cursor, CancellationToken cancellationToken)
    {
        // Id comes from the route, so limit/cursor are bound manually: putting the whole
        // query object under [AsParameters] would make ImportId a required query parameter.
        var result = await sender.Send(new ListImportErrorsQuery
        {
            ImportId = id,
            Limit = limit,
            Cursor = cursor,
        }, cancellationToken);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Cancel an import and surgically delete its unedited transactions")]
    public static async Task<NoContent> CancelImport(
        ISender sender, Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelImportCommand { ImportId = id }, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<string?> ReadFirstNonEmptyLineAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }
        return null;
    }
}