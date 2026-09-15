using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Retries only cancellation control messages. Delivery follow-ups themselves remain scheduled and sent by Twilio.
/// </summary>
public sealed class ScheduledMessageCancellationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopeFactory;

    public ScheduledMessageCancellationWorker(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RetryAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // A later tick retries persisted cancellation intent. No destination or content is logged.
            }
        }
    }

    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var coordinator = scope.ServiceProvider.GetRequiredService<OrderNotificationCoordinator>();
        var orderIds = await context.OrderNotifications
            .Where(x => x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null &&
                        x.ProviderStatus != "canceled")
            .Join(context.Orders.Where(x => x.Status == OrderStatus.Cancelled),
                notification => notification.OrderId, order => order.Id, (notification, order) => order.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var orderId in orderIds)
            await coordinator.CancelScheduledForOrderAsync(orderId, null, cancellationToken);
    }
}
