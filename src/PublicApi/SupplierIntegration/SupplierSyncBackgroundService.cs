using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierIntegration;

/// <summary>
/// Hosted worker that drains the <see cref="ISupplierSyncQueue"/> and runs each supplier sync in
/// its own DI scope, so a sync started by an API call completes independently of the request that
/// started it.
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
        _logger.LogInformation("Supplier sync background worker started.");

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
                var syncService = scope.ServiceProvider.GetRequiredService<ISupplierCatalogSyncService>();
                await syncService.RunSyncAsync(syncId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A sync failure is recorded on the sync record itself; this is the last-resort net
                // so one bad run never takes the worker down.
                _logger.LogError(ex, "Unhandled error while running supplier sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync background worker stopping.");
    }
}
