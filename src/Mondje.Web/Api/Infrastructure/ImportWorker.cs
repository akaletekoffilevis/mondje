using System.Threading.Channels;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Imports.Commands.ProcessImport;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Mondje.Web.Api.Infrastructure;

/// <summary>
/// Drains the import queue with several parallel processors — challenge 6 requires three
/// files processed simultaneously with visible progress, bonus 4 goes up to ten
/// (Import:MaxParallel). Each batch runs in its own DI scope, hence its own DbContext.
/// Batches left PENDING or RUNNING by a crashed process are resumed on startup (12).
/// </summary>
public class ImportWorker : BackgroundService
{
    private const int DefaultMaxParallel = 3;

    private readonly ImportQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImportWorker> _logger;

    public ImportWorker(
        ImportQueue queue,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ImportWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resume anything left over from a previous (possibly crashed) run.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var toResume = await db.ImportBatches
                .AsNoTracking()
                .Where(b => b.Status == ImportBatch.Pending || b.Status == ImportBatch.Running)
                .Select(b => b.Id)
                .ToListAsync(stoppingToken);

            foreach (var id in toResume)
            {
                _queue.Enqueue(id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pending imports for resume.");
        }

        var maxParallel = Math.Max(1, _configuration.GetValue("Import:MaxParallel", DefaultMaxParallel));
        _logger.LogInformation("Import worker: {MaxParallel} parallel processors.", maxParallel);

        var processors = new Task[maxParallel];
        for (var i = 0; i < maxParallel; i++)
        {
            processors[i] = Task.Run(() => ConsumeAsync(stoppingToken), stoppingToken);
        }

        await Task.WhenAll(processors);
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await sender.Send(new ProcessImportCommand { ImportId = id }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import {ImportId} failed.", id);
            }
        }
    }
}
