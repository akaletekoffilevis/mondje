using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mondje.Infrastructure.Data;

/// <summary>
/// Design-time factory used by the EF Core CLI ("dotnet ef migrations add") so migrations
/// can be created from the Infrastructure project without booting the API.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MondjeDb")
            ?? "Server=localhost,1433;Database=MondjeDb;User Id=sa;Password=Mondje2026!;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
