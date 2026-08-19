using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierSync;

/// <summary>
/// Drains queued supplier syncs and runs each one in its own DI scope (so it gets a fresh
/// <c>DbContext</c>), letting the start-sync endpoint return before the work completes.
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
                break; // host is shutting down
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<ISupplierCatalogImporter>();
                await importer.ProcessAsync(syncId, stoppingToken);
            }
            catch (Exception ex)
            {
                // ProcessAsync already records failure on the sync record; this is a last-resort guard
                // so one bad sync never takes the worker down.
                _logger.LogError(ex, "Unhandled error while processing supplier sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync background worker stopping.");
    }
}
