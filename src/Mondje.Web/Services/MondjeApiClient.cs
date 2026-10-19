using System.Net.Http.Json;

namespace Mondje.Web.Services;

/// <summary>
/// Thin HTTP client used by the MVC frontend to consume the Mondje API.
/// The base URL comes from the "Api:BaseUrl" configuration key.
/// </summary>
public class MondjeApiClient(HttpClient httpClient)
{
    public async Task<ApiStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<ApiStatus>("/api/Status", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}

public record ApiStatus(string status, DateTimeOffset utc, string? version);
