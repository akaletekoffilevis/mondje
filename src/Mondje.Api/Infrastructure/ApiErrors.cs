using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Mondje.Api.Infrastructure;

/// <summary>422 responses with a French reason and no technical trace (Mondje rules).</summary>
public static class ApiErrors
{
    public static ProblemHttpResult Unprocessable(string field, string message) =>
        TypedResults.Problem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [field] = [message],
        })
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "One or more validation errors occurred.",
        });
}
