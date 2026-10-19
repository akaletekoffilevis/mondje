using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mondje.Infrastructure.Data;

public static class InitialiserExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync();
    }
}

public class ApplicationDbContextInitialiser(
    ILogger<ApplicationDbContextInitialiser> logger,
    ApplicationDbContext context)
{
    /// <summary>
    /// Applies pending migrations, creating the database if needed. Data is never dropped
    /// automatically: imports must survive server restarts (challenge 12 - resume after crash).
    /// </summary>
    public async Task InitialiseAsync()
    {
        try
        {
            // Simple recovery model: a large import is a bulk operation, and minimal
            // logging (bulk insert under a table lock, data written in key order) is only
            // possible when the database is not in full recovery.
            await context.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET RECOVERY SIMPLE");

            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }
}
