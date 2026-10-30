using Mondje.Application.Dashboard.GetDashboard;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mondje.Web.Api.Endpoints;

/// <summary>
/// Minimal health/status endpoint (used by Mondje.Web to display API availability) and
/// the aggregated dashboard figures (challenge 15).
/// </summary>
public class Status : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetStatus);
        groupBuilder.MapGet(GetDashboard, "dashboard");
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

    [EndpointSummary("Dashboard: ledger totals, import outcomes and per-agency figures")]
    public static async Task<Ok<DashboardDto>> GetDashboard(
        ISender sender, CancellationToken cancellationToken)
        => TypedResults.Ok(await sender.Send(new GetDashboardQuery(), cancellationToken));
}
