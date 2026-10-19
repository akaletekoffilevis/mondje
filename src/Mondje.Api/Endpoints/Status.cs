namespace Mondje.Api.Endpoints;

/// <summary>
/// Minimal health/status endpoint. Also used by Mondje.Web to display API availability.
/// </summary>
public class Status : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetStatus);
    }

    [EndpointSummary("API status")]
    public static IResult GetStatus(TimeProvider time)
    {
        return Results.Ok(new
        {
            status = "ok",
            utc = time.GetUtcNow(),
            version = typeof(Status).Assembly.GetName().Version?.ToString()
        });
    }
}
