using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Drains the <see cref="ISupplierSyncQueue"/> and runs each queued sync in its own DI scope, so
/// starting a sync from the API returns immediately while the actual listing read and import
/// happen in the background. Syncs run one at a time.
/// </summary>
public class SupplierSyncBackgroundService : BackgroundService
{
    private readonly ISupplierSyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupplierSyncBackgroundService> _logger;

    public SupplierSyncBackgroundService(
        ISupplierSyncQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SupplierSyncBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Supplier sync worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            int syncId;
            try
            {
                syncId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISupplierCatalogSyncService>();
                await service.ExecuteAsync(syncId, stoppingToken);
            }
            catch (Exception ex)
            {
                // The sync service records its own failures; this only guards the worker loop so a
                // single bad sync can't take the worker down.
                _logger.LogError(ex, "Unhandled error running supplier sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync worker stopping.");
    }
}
