using System.Diagnostics;

namespace Mondje.Web.Api.Infrastructure;

/// <summary>
/// Rule: "chaque requête porte un identifiant dans les logs". Every request gets an id:
/// the incoming X-Request-Id header is reused when it is sane, otherwise a fresh one is
/// minted. The id becomes the TraceIdentifier (so the import upload stores it on the
/// batch), is echoed back as the X-Request-Id response header, and exactly one log line
/// is written per request with the id, method, path, final status code and duration.
/// </summary>
public class RequestIdMiddleware
{
    public const string HeaderName = "X-Request-Id";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestIdMiddleware> _logger;

    public RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = ResolveRequestId(context);
        context.TraceIdentifier = requestId;

        var start = Stopwatch.GetTimestamp();
        var logged = false;

        // Logged at response start so the line carries the final status code, including
        // 404/409/422/500 responses produced by the exception handler further up.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            if (!logged)
            {
                logged = true;
                Log(context, requestId, start);
            }

            return Task.CompletedTask;
        });

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Server-side detail only; the client still receives a generic problem body.
            _logger.LogError(
                ex,
                "Requête {RequestId} {Method} {Path} a échoué",
                requestId,
                context.Request.Method,
                context.Request.Path);
            throw;
        }
        finally
        {
            // The exception path logs its own line above, and the OnStarting callback
            // above covers every response that reached the client.
            if (!logged && context.Response.HasStarted == false && !context.Items.ContainsKey("RequestIdFailed"))
            {
                logged = true;
                Log(context, requestId, start);
            }
        }
    }

    private void Log(HttpContext context, string requestId, long start) =>
        _logger.LogInformation(
            "Requête {RequestId} {Method} {Path} -> {StatusCode} en {ElapsedMs} ms",
            requestId,
            context.Request.Method,
            context.Request.Path + context.Request.QueryString,
            context.Response.StatusCode,
            Stopwatch.GetElapsedTime(start));

    private static string ResolveRequestId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            var candidate = values.ToString();
            if (IsSane(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    /// <summary>Bounded and restricted to log-safe characters to prevent log injection.</summary>
    private static bool IsSane(string value) =>
        value.Length is > 0 and <= MaxLength &&
        value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
