using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Services;

internal sealed class ScheduledMessageCancellationWorker(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CancelUnsafeFollowUpsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // The durable cancellation_pending state is retried on the next pass.
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task CancelUnsafeFollowUpsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IMessagingProvider>();

        var candidates = await (
            from notification in context.OrderNotifications
            join order in context.Orders on notification.OrderId equals order.Id
            join contact in context.ContactNumbers on notification.ContactNumberId equals contact.Id
            where notification.Kind == NotificationKind.DeliveryFollowUp &&
                  notification.ProviderMessageSid != null &&
                  notification.ProviderStatus != "canceled" &&
                  notification.ProviderStatus != "delivered" &&
                  notification.ProviderStatus != "sent" &&
                  (order.Status == OrderStatus.Cancelled || !contact.IsActive)
            select notification).ToListAsync(cancellationToken);

        foreach (var notification in candidates)
        {
            try
            {
                notification.RecordProviderResult(
                    await provider.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken),
                    DateTimeOffset.UtcNow);
            }
            catch (MessagingProviderException ex)
            {
                notification.RecordFailure("cancellation_pending", ex.Message, DateTimeOffset.UtcNow);
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }
    }
}
