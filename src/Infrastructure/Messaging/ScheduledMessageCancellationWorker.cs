using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class ScheduledMessageCancellationWorker(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RetryInterval);
        do
        {
            await CancelPendingAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CancelPendingAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        CatalogContext context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        IMessageProvider provider = scope.ServiceProvider.GetRequiredService<IMessageProvider>();
        int[] cancelledOrderIds = await context.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.Cancelled)
            .Select(order => order.Id)
            .ToArrayAsync(cancellationToken);
        if (cancelledOrderIds.Length == 0)
        {
            return;
        }

        var pending = await context.OrderNotifications
            .Where(notification =>
                cancelledOrderIds.Contains(notification.OrderId) &&
                notification.Kind == NotificationKind.DeliveryFollowUp &&
                notification.ProviderSid != null &&
                notification.ProviderStatus == "provider_error")
            .ToListAsync(cancellationToken);
        foreach (OrderNotification notification in pending)
        {
            try
            {
                ProviderMessage state = await provider.CancelAsync(notification.ProviderSid!, cancellationToken);
                notification.ApplyProviderState(
                    state.Sid,
                    state.Status,
                    state.ErrorCode,
                    state.DateCreated,
                    state.DateSent,
                    state.DateUpdated,
                    DateTimeOffset.UtcNow);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (MessageProviderException)
            {
                // The persisted provider_error state keeps this cancellation eligible for the next retry.
            }
        }
    }
}
