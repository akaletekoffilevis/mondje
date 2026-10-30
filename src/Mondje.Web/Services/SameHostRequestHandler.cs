using Microsoft.AspNetCore.Http;

namespace Mondje.Web.Services;

/// <summary>
/// Rewrites outgoing calls from the web front end to the public host of the current request.
/// The API is hosted in the same application, so calling a fixed <c>http://localhost:port</c>
/// would fail under IIS (in-process hosting uses a dynamic port and no local Kestrel listener).
/// Using the request's scheme, host and port keeps the call on the same origin everywhere.
/// </summary>
public sealed class SameHostRequestHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        if (context is not null && request.RequestUri is { } uri)
        {
            // Build the target URI by string concatenation: UriBuilder would otherwise
            // tamper with the already-escaped path and reject query portions.
            var authority = context.Request.Host.ToUriComponent();
            request.RequestUri = new Uri($"{context.Request.Scheme}://{authority}{uri.PathAndQuery}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
