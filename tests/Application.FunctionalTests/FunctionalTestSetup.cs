using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mondje.Application.FunctionalTests;

[SetUpFixture]
public class FunctionalTestSetup
{
    internal static IServiceScopeFactory ScopeFactory { get; private set; } = null!;
    internal static DatabaseResetter? DbResetter { get; private set; }
    internal static HttpClient Client { get; private set; } = null!;

    private static WebApiFactory? _factory;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MondjeDb")
            ?? "Server=localhost,1433;Database=MondjeDb_Test;User Id=sa;Password=Mondje2026!;TrustServerCertificate=True";

        _factory = new WebApiFactory(connectionString);
        Client = _factory.CreateClient();
        ScopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        DbResetter = await DatabaseResetter.CreateAsync(connectionString);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (DbResetter is not null) await DbResetter.DisposeAsync();
        Client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }
}
