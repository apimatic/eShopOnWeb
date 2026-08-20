using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationDispatcher
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(4);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationDispatcher> _logger;

    public OrderNotificationDispatcher(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationDispatcher> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ContactNumber?> GetDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumbers.FirstOrDefaultAsync(new LatestContactNumberForBuyerSpec(buyerId), cancellationToken);
    }

    public async Task NotifyAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        int? parentNotificationId,
        string? idempotencyKey,
        string? destinationOverride,
        CancellationToken cancellationToken)
    {
        var destination = destinationOverride;
        if (string.IsNullOrWhiteSpace(destination))
        {
            var contact = await GetDestinationAsync(buyerId, cancellationToken);
            destination = contact?.PhoneNumber;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            destination,
            body,
            parentNotificationId,
            idempotencyKey,
            sendAt);

        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            SmsMessageSnapshot snapshot;
            if (sendAt is DateTimeOffset scheduled)
            {
                snapshot = await _smsGateway.ScheduleAsync(destination, body, scheduled, cancellationToken);
            }
            else
            {
                snapshot = await _smsGateway.SendImmediateAsync(destination, body, cancellationToken);
            }

            ApplySnapshot(notification, snapshot, body);
            await _notifications.UpdateAsync(notification, cancellationToken);

            if (!snapshot.Succeeded)
            {
                _logger.LogWarning(
                    "SMS notification {NotificationId} for order {OrderId} did not send. Status={Status} Sid={Sid}",
                    notification.Id, orderId, notification.Status, notification.ProviderSid ?? string.Empty);
            }
        }
        catch (Exception)
        {
            notification.MarkSendFailed("The messaging provider could not be reached.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning(
                "SMS notification {NotificationId} for order {OrderId} failed unexpectedly.",
                notification.Id, orderId);
        }
    }

    public async Task RefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var snapshot = await _smsGateway.FetchAsync(notification.ProviderSid, cancellationToken);
            if (!snapshot.Succeeded && snapshot.Sid is null)
            {
                return;
            }

            ApplySnapshot(notification, snapshot, notification.ContentDisposed ? null : snapshot.Body ?? notification.Body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Could not refresh provider state for notification {NotificationId}.",
                notification.Id);
        }
    }

    public async Task CancelFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderSid))
            {
                continue;
            }

            if (string.Equals(followUp.Status, "canceled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(followUp.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(followUp.Status, "sent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(followUp.Status, "delivered", StringComparison.OrdinalIgnoreCase)
                || string.Equals(followUp.Status, "undelivered", StringComparison.OrdinalIgnoreCase)
                || string.Equals(followUp.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                ApplySnapshot(followUp, snapshot, followUp.ContentDisposed ? null : followUp.Body);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Could not cancel scheduled follow-up {NotificationId} for order {OrderId}.",
                    followUp.Id, orderId);
            }
        }
    }

    public static string PlacedBody(int orderId) =>
        $"Your eShopOnWeb order #{orderId} has been placed. Thank you for your purchase.";

    public static string DispatchedBody(int orderId) =>
        $"Your eShopOnWeb order #{orderId} is on its way.";

    public static string FollowUpBody(int orderId) =>
        $"How did the delivery of your eShopOnWeb order #{orderId} go? We would love to hear from you.";

    public static string CancelledBody(int orderId) =>
        $"Your eShopOnWeb order #{orderId} has been cancelled.";

    private static void ApplySnapshot(OrderNotification notification, SmsMessageSnapshot snapshot, string? body)
    {
        if (snapshot.Succeeded || snapshot.Sid is not null)
        {
            notification.ApplyProviderState(
                snapshot.Sid,
                snapshot.Status,
                snapshot.ErrorCode,
                snapshot.ErrorMessage,
                snapshot.DateSent,
                snapshot.DateCreated,
                notification.ContentDisposed ? null : body);
            return;
        }

        notification.MarkSendFailed(snapshot.ErrorMessage ?? "The messaging provider rejected the send.");
        if (snapshot.ErrorCode is int code)
        {
            notification.ApplyProviderState(null, "failed", code, snapshot.ErrorMessage, snapshot.DateSent, snapshot.DateCreated, null);
        }
    }
}
