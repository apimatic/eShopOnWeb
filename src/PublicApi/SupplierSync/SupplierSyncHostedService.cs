using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>
/// Drains the <see cref="ISyncQueue"/> and executes each queued sync in its own DI scope. Syncs run
/// one at a time, which keeps the shared catalog store consistent (e.g. no racing to create the same
/// brand) while letting the endpoint that started the sync return immediately.
/// </summary>
public class SupplierSyncHostedService : BackgroundService
{
    private readonly ISyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupplierSyncHostedService> _logger;

    public SupplierSyncHostedService(
        ISyncQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SupplierSyncHostedService> logger)
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ISupplierCatalogSyncService>();
                await syncService.ExecuteSyncAsync(syncId, stoppingToken);
            }
            catch (Exception ex)
            {
                // ExecuteSyncAsync records its own failures; this guards the worker loop itself so a
                // single bad run can never take the worker down.
                _logger.LogError(ex, "Unhandled error while executing sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync worker stopping.");
    }
}
