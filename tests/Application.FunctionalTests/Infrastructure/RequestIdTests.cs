using System.Net;

namespace Mondje.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Rule: every request carries an identifier in the logs. The id is echoed to the client
/// as X-Request-Id so a user can quote it, and reused verbatim when sane.
/// </summary>
public class RequestIdTests : TestBase
{
    [Test]
    public async Task Every_response_carries_an_x_request_id()
    {
        var response = await Client.GetAsync("/api/Status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Request-Id", out var values).ShouldBeTrue();
        values!.Single().ShouldNotBeEmpty();
    }

    [Test]
    public async Task A_client_supplied_id_is_reused()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/Status");
        request.Headers.Add("X-Request-Id", "monid-001");

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Request-Id").Single().ShouldBe("monid-001");
    }

    [Test]
    public async Task A_malformed_client_id_is_replaced_and_404_is_still_identified()
    {
        // UUID bien formé mais absent : la route répond 404, et la réponse reste identifiée.
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/Transactions/bbbbbbbb-0000-4000-8000-000000000001");
        request.Headers.Add("X-Request-Id", "id avec des espaces interdits");

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.GetValues("X-Request-Id").Single()
            .ShouldNotBe("id avec des espaces interdits");
    }
}
