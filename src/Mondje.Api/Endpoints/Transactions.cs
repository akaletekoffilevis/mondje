using Mondje.Application.Transactions.Commands.CreateTransaction;
using Mondje.Application.Transactions.Commands.DeleteTransaction;
using Mondje.Application.Transactions.Commands.RestoreTransaction;
using Mondje.Application.Transactions.Commands.UpdateTransaction;
using Mondje.Application.Transactions.Common;
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
}
