using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Mondje.Web.Api.Infrastructure;

/// <summary>
/// Adds standard error responses to every OpenAPI operation. A 422 Unprocessable Content is added
/// to all operations because every request passes through <c>ValidationBehaviour</c> in the MediatR
/// pipeline.
/// </summary>
internal sealed class ApiExceptionOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= [];
        operation.Responses.TryAdd("422", new OpenApiResponse { Description = "Unprocessable Content" });

        return Task.CompletedTask;
    }
}
