using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.CatalogSync;

/// <summary>
/// Drains the <see cref="ISupplierSyncQueue"/> and runs each queued sync in its own DI scope, so
/// a sync executes after its HTTP request has returned and gets a fresh, scoped DbContext.
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
                var processor = scope.ServiceProvider.GetRequiredService<ISupplierCatalogSyncService>();
                await processor.ProcessSyncAsync(syncId, stoppingToken);
            }
            catch (Exception ex)
            {
                // Never let one sync take down the worker loop.
                _logger.LogError(ex, "Unhandled error processing supplier sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync background worker stopping.");
    }
}
