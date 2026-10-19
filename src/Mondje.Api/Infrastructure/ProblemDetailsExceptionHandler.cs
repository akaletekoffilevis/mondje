using Mondje.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NotFoundException = Mondje.Application.Common.Exceptions.NotFoundException;

namespace Mondje.Api.Infrastructure;

/// <summary>
/// Converts well-known application exceptions into RFC 9110-compliant <see cref="ProblemDetails"/> responses.
/// <see cref="ValidationException"/> maps to 422 Unprocessable Content, as required by the Mondje rules.
/// Any other exception is never leaked to the client: a generic 500 without technical details is returned.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, problem) = exception switch
        {
            ValidationException ve => (StatusCodes.Status422UnprocessableEntity, (object)new ValidationProblemDetails(ve.Errors)
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "One or more validation failures have occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.13"
            }),
            NotFoundException ne => (StatusCodes.Status404NotFound, new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource not found.",
                Detail = ne.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5"
            }),
            ConflictException ce => (StatusCodes.Status409Conflict, new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict.",
                Detail = ce.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            }),
            _ => (0, null)
        };

        if (statusCode == 0)
        {
            // Never expose technical details to the client. The full exception is logged server-side.
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            }, cancellationToken);

            return true;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
