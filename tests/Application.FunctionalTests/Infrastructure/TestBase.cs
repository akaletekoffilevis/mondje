namespace Mondje.Application.FunctionalTests.Infrastructure;

public abstract class TestBase
{
    /// <summary>Shared HTTP client bound to the API under test (in-memory TestServer).</summary>
    protected static HttpClient Client => FunctionalTestSetup.Client;

    [SetUp]
    public async Task SetUp()
    {
        if (FunctionalTestSetup.DbResetter is not null)
        {
            await FunctionalTestSetup.DbResetter.ResetAsync();
        }
    }
}
