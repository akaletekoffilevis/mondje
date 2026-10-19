using System.Threading.Channels;
using Mondje.Application.Common.Interfaces;
using Mondje.Application.Imports.Commands.ProcessImport;
using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Mondje.Api.Infrastructure;

/// <summary>
/// Background worker that drains the import queue and resumes batches left pending or
/// running after a crash (challenge 12: reprise après incident).
/// </summary>
public class ImportWorker : BackgroundService
{
    private readonly ImportQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportWorker> _logger;

    public ImportWorker(ImportQueue queue, IServiceScopeFactory scopeFactory, ILogger<ImportWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
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

        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await sender.Send(new ProcessImportCommand { ImportId = id }, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import {ImportId} failed.", id);
            }
        }
    }
}