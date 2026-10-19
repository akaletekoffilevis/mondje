namespace Mondje.Application.FunctionalTests.Infrastructure;

public abstract class TestBase
{
    [SetUp]
    public async Task SetUp()
    {
        if (FunctionalTestSetup.DbResetter is not null)
        {
            await FunctionalTestSetup.DbResetter.ResetAsync();
        }
    }
}
