using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.BackgroundTasks;

/// <summary>
/// Drains the sync queue and processes one supplier sync at a time. Each sync runs in its own DI
/// scope (and therefore its own DbContext), so background work never shares state with request
/// handling.
/// </summary>
public class SupplierSyncHostedService : BackgroundService
{
    private readonly IBackgroundSyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupplierSyncHostedService> _logger;

    public SupplierSyncHostedService(
        IBackgroundSyncQueue queue,
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
                var processor = scope.ServiceProvider.GetRequiredService<ISupplierCatalogSyncProcessor>();
                await processor.ProcessAsync(syncId, stoppingToken);
            }
            catch (Exception ex)
            {
                // The processor already records failures against the sync; this is the last-resort net.
                _logger.LogError(ex, "Unhandled error while processing supplier sync {SyncId}.", syncId);
            }
        }

        _logger.LogInformation("Supplier sync worker stopping.");
    }
}
