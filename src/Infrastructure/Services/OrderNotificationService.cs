using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private readonly CatalogContext _context;
    private readonly ISmsProvider _provider;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(CatalogContext context, ISmsProvider provider, TimeProvider timeProvider)
    {
        _context = context;
        _provider = provider;
        _timeProvider = timeProvider;
    }

    public async Task SendOrderEventAsync(
        Order order,
        NotificationKind kind,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var contacts = await _context.RegisteredContactNumbers
            .Where(contact => contact.BuyerId == order.BuyerId && contact.RemovedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(
                order.Id,
                contact.Id,
                kind,
                BuildMessage(kind, order.Id),
                _timeProvider.GetUtcNow(),
                scheduledFor);

            _context.OrderNotifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);
            await SendReservedNotificationAsync(notification, contact.CanonicalNumber, cancellationToken);
        }
    }

    public async Task CancelScheduledFollowUpsAsync(
        int orderId,
        int? contactNumberId,
        CancellationToken cancellationToken)
    {
        var query = _context.OrderNotifications.Where(notification =>
            notification.OrderId == orderId &&
            notification.Kind == NotificationKind.DeliveryFollowUp &&
            notification.CancellationRequestedAt == null &&
            notification.ProviderStatus != NotificationDeliveryStatus.Canceled);

        if (contactNumberId is not null)
        {
            query = query.Where(notification => notification.ContactNumberId == contactNumberId.Value);
        }

        var notifications = await query.ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            notification.RequestCancellation(_timeProvider.GetUtcNow());
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
    }

    public async Task CancelOutstandingScheduledMessagesAsync(CancellationToken cancellationToken)
    {
        var notifications = await _context.OrderNotifications
            .Where(notification => notification.CancellationRequestedAt != null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await TryCancelAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshAsync(
        IReadOnlyCollection<OrderNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(item => item.ProviderMessageSid is not null))
        {
            try
            {
                var state = await _provider.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(state, _timeProvider.GetUtcNow());
            }
            catch
            {
                // The most recently observed provider state remains reportable when polling is unavailable.
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(
        OrderNotification original,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await _context.OrderNotifications.FirstOrDefaultAsync(notification =>
            notification.OriginalNotificationId == original.Id &&
            notification.ResendIdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (!CanResend(original.ProviderStatus))
        {
            throw new InvalidOperationException("Only a failed or undelivered notification can be resent.");
        }

        if (original.Body is null)
        {
            throw new InvalidOperationException("A notification whose content was disposed of cannot be resent.");
        }

        var contact = await _context.RegisteredContactNumbers
            .FirstOrDefaultAsync(item => item.Id == original.ContactNumberId && item.RemovedAt == null, cancellationToken)
            ?? throw new InvalidOperationException("The destination is no longer registered.");

        var notification = new OrderNotification(
            original.OrderId,
            original.ContactNumberId,
            NotificationKind.Resend,
            original.Body,
            _timeProvider.GetUtcNow(),
            originalNotificationId: original.Id,
            resendIdempotencyKey: idempotencyKey);

        _context.OrderNotifications.Add(notification);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(notification).State = EntityState.Detached;
            return await _context.OrderNotifications.SingleAsync(item =>
                item.OriginalNotificationId == original.Id && item.ResendIdempotencyKey == idempotencyKey,
                cancellationToken);
        }

        await SendReservedNotificationAsync(notification, contact.CanonicalNumber, cancellationToken);
        return notification;
    }

    public async Task RedactAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.Body is null)
        {
            return;
        }

        if (notification.ProviderMessageSid is not null)
        {
            var providerMessage = await _provider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(providerMessage, _timeProvider.GetUtcNow());
            if (!string.IsNullOrEmpty(providerMessage.Body))
            {
                throw new InvalidOperationException("The provider did not confirm message-content redaction.");
            }
        }

        notification.Redact(_timeProvider.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task SendReservedNotificationAsync(
        OrderNotification notification,
        string destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _provider.SendAsync(destination, notification.Body!, notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(result, _timeProvider.GetUtcNow());
        }
        catch (SmsProviderException exception)
        {
            notification.RecordProviderRequestFailure(exception.ProviderErrorCode, _timeProvider.GetUtcNow());
        }
        catch
        {
            // A timeout is deliberately not retried: Twilio's create-message operation is not idempotent.
            notification.RecordProviderRequestFailure(null, _timeProvider.GetUtcNow());
        }

        await _context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null)
        {
            notification.ClearCancellationRequest();
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var state = await _provider.CancelAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(state, _timeProvider.GetUtcNow());
            notification.ClearCancellationRequest();
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                var state = await _provider.GetAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordProviderState(state, _timeProvider.GetUtcNow());
                if (!string.Equals(state.Status, NotificationDeliveryStatus.Scheduled, StringComparison.OrdinalIgnoreCase))
                {
                    notification.ClearCancellationRequest();
                }
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Leave CancellationRequestedAt set so the hosted retry worker tries again.
            }
        }
    }

    private static bool CanResend(string status) =>
        string.Equals(status, NotificationDeliveryStatus.ProviderRequestFailed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, NotificationDeliveryStatus.Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, NotificationDeliveryStatus.Undelivered, StringComparison.OrdinalIgnoreCase);

    private static string BuildMessage(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: order #{orderId} was placed successfully.",
        NotificationKind.OrderDispatched => $"eShopOnWeb: order #{orderId} has been dispatched and is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did delivery of order #{orderId} go?",
        NotificationKind.OrderCancelled => $"eShopOnWeb: order #{orderId} has been cancelled.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
