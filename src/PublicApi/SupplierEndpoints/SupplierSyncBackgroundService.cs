using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Drains the sync-job queue and runs each sync in its own DI scope (so it gets its own DbContext),
/// letting the <c>POST .../sync</c> request return before the sync completes.
/// </summary>
public class SupplierSyncBackgroundService : BackgroundService
{
    private readonly ISyncJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupplierSyncBackgroundService> _logger;

    public SupplierSyncBackgroundService(
        ISyncJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SupplierSyncBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ISupplierSyncProcessor>();
            try
            {
                await processor.ProcessAsync(syncId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while processing sync {SyncId}", syncId);
            }
        }
    }
}
