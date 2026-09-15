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

namespace Microsoft.eShopWeb.PublicApi.Notifications;

// This worker never sends a notification. It only retries provider-side cancellation of a
// follow-up already queued at Twilio when an order cancellation encountered a transient outage.
public sealed class ScheduledMessageCancellationWorker : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;

    public ScheduledMessageCancellationWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RetryAsync(stoppingToken);
                await Task.Delay(RetryInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // A later pass retries durable cancellation_failed records.
                await Task.Delay(RetryInterval, stoppingToken);
            }
        }
    }

    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var twilio = scope.ServiceProvider.GetRequiredService<ITwilioGateway>();
        var canceledOrderIds = db.Orders.Where(x => x.Status == OrderStatus.Cancelled).Select(x => x.Id);
        var pending = await db.OrderNotifications.Where(x =>
            canceledOrderIds.Contains(x.OrderId) && x.Kind == NotificationKind.DeliveryFollowUp &&
            x.ProviderStatus == "cancellation_failed" && x.ProviderMessageSid != null &&
            x.ProviderDateSent == null).ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            try
            {
                var result = await twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode,
                    result.ErrorMessage, result.DateSent);
            }
            catch (TwilioProviderException ex)
            {
                notification.RecordCancellationFailure(ex.ProviderCode, ex.ProviderMessage);
            }
            catch (Exception)
            {
                notification.RecordCancellationFailure(null, "The messaging provider was unavailable during cancellation.");
            }
        }

        if (pending.Count > 0) await db.SaveChangesAsync(CancellationToken.None);
    }
}
