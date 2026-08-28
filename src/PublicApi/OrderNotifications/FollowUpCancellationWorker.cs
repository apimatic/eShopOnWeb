using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class FollowUpCancellationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FollowUpCancellationWorker> _logger;

    public FollowUpCancellationWorker(IServiceScopeFactory scopeFactory, ILogger<FollowUpCancellationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
                var service = scope.ServiceProvider.GetRequiredService<OrderNotificationService>();
                var pending = await db.OrderNotifications
                    .Where(x => x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderStatus == "cancel-pending")
                    .ToListAsync(stoppingToken);
                foreach (var notification in pending)
                    await service.RetryFollowUpCancellationAsync(notification);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                _logger.LogWarning("The follow-up cancellation retry pass failed; it will run again.");
            }
        }
    }
}
