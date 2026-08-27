using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class NotificationCoordinator
{
    private readonly CatalogContext _db;
    private readonly ITwilioGateway _twilio;

    public NotificationCoordinator(CatalogContext db, ITwilioGateway twilio)
    {
        _db = db;
        _twilio = twilio;
    }

    public async Task SendToActiveNumbersAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var numbers = await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == order.BuyerId).ToListAsync(cancellationToken);

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.Id,
                number.CanonicalNumber, kind, body, DateTimeOffset.UtcNow);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SubmitAsync(notification, scheduledFor, cancellationToken);
        }
    }

    public async Task SubmitAsync(OrderNotification notification, DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _twilio.SendMessageAsync(notification.Destination,
                notification.Body ?? string.Empty, scheduledFor, cancellationToken);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode,
                result.ErrorMessage, result.DateSent, scheduledFor);
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordSubmissionFailure(ex.ProviderCode, SafeOutcome(ex));
        }
        catch (Exception)
        {
            notification.RecordSubmissionFailure(null, "The messaging provider was unavailable.");
        }

        await _db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => !string.IsNullOrWhiteSpace(x.ProviderMessageSid)))
        {
            try
            {
                var result = await _twilio.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode,
                    result.ErrorMessage, result.DateSent);
                changed = true;
            }
            catch (Exception)
            {
                // A read failure leaves the durable last-known provider state intact.
            }
        }

        if (changed) await _db.SaveChangesAsync(CancellationToken.None);
    }

    public static NotificationDto ToDto(OrderNotification x) => new(x.Id, x.OrderId,
        x.Kind.ToString(), x.ProviderMessageSid, x.ProviderStatus, x.ProviderErrorCode,
        x.ProviderErrorMessage, x.Body, x.CreatedAt, x.ScheduledFor, x.ProviderDateSent,
        x.ContentDisposedAt, x.ResendsNotificationId);

    private static string SafeOutcome(TwilioProviderException ex) =>
        string.IsNullOrWhiteSpace(ex.ProviderMessage) ? "Twilio rejected the message." : ex.ProviderMessage;
}

public sealed record NotificationDto(int NotificationId, int OrderId, string Kind,
    string? ProviderMessageSid, string ProviderStatus, int? ProviderErrorCode,
    string? ProviderErrorMessage, string? Content, DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor, DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ContentDisposedAt, int? ResendsNotificationId);
