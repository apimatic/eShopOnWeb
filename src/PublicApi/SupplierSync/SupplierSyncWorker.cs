using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>
/// Hosted background service that drains the supplier sync queue and executes each sync in a
/// fresh dependency-injection scope (so it has its own DbContext, independent of the request
/// that started it).
/// </summary>
public class SupplierSyncWorker : BackgroundService
{
    private readonly ISupplierSyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupplierSyncWorker> _logger;

    public SupplierSyncWorker(
        ISupplierSyncQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SupplierSyncWorker> logger)
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
            Guid syncId;
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
                await syncService.ExecuteAsync(syncId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The sync service records failures on the sync record itself; this is a last
                // resort so one bad sync can't take the worker down.
                _logger.LogError(ex, "Unhandled error while running supplier sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync worker stopping.");
    }
}
