using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Drains <see cref="ICatalogSyncQueue"/> and executes each queued sync on its own DI scope,
/// so the "start sync" request can return before the sync finishes. A failing sync is recorded
/// on its own record by the sync service and never brings the worker down.
/// </summary>
public class CatalogSyncBackgroundService : BackgroundService
{
    private readonly ICatalogSyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogSyncBackgroundService> _logger;

    public CatalogSyncBackgroundService(
        ICatalogSyncQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CatalogSyncBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Catalog sync background worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            int syncId;
            try
            {
                syncId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ISupplierCatalogSyncService>();
                await syncService.RunSyncAsync(syncId, stoppingToken);
            }
            catch (Exception ex)
            {
                // The sync service already records failures on the sync record; this is a
                // last-resort guard so one bad run can't stop the worker.
                _logger.LogError(ex, "Unhandled error while running catalog sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Catalog sync background worker stopping.");
    }
}
