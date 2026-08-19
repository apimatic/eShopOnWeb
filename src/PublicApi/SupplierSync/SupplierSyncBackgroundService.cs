using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>
/// Drains <see cref="ISupplierSyncQueue"/> and runs each sync via <see cref="ISupplierCatalogSyncService"/>
/// in its own dependency-injection scope, so the sync endpoint can return without waiting for the
/// crawl and import to finish.
/// </summary>
public sealed class SupplierSyncBackgroundService : BackgroundService
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
        _logger.LogInformation("Supplier sync background worker started.");

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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // RunSyncAsync already records failures on the sync record; this is a last-resort
                // guard so one bad sync can never take the worker down.
                _logger.LogError(ex, "Unhandled error while running catalog sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync background worker stopping.");
    }
}
