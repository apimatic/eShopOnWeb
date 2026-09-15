using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class OrderNotificationPublisher
{
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IMessagingProvider _messaging;
    private readonly IAppLogger<OrderNotificationPublisher> _logger;

    public OrderNotificationPublisher(
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IMessagingProvider messaging,
        IAppLogger<OrderNotificationPublisher> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<string?> GetDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.Count == 0 ? null : numbers[0].CanonicalNumber;
    }

    public async Task TrySendAsync(
        int orderId,
        string buyerId,
        string kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await GetDestinationAsync(buyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("No contact number on file for buyer {BuyerId}; skipping {Kind} for order {OrderId}.", buyerId, kind, orderId);
            return;
        }

        try
        {
            ProviderMessage message;
            if (sendAt is null)
            {
                message = await _messaging.SendAsync(destination, body, cancellationToken);
            }
            else
            {
                message = await _messaging.ScheduleAsync(destination, body, sendAt.Value, cancellationToken);
            }

            var record = new OrderNotification(
                orderId,
                buyerId,
                kind,
                destination,
                body,
                message.Sid,
                message.Status,
                message.ErrorCode,
                message.ErrorMessage,
                sendAt);

            await _notifications.AddAsync(record, cancellationToken);
            _logger.LogInformation("Recorded {Kind} notification {NotificationId} for order {OrderId}.", kind, record.Id, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send {Kind} for order {OrderId}: {Reason}", kind, orderId, SafeReason(ex));
            var failed = new OrderNotification(
                orderId,
                buyerId,
                kind,
                destination,
                body,
                providerSid: null,
                providerStatus: null,
                errorCode: null,
                errorMessage: null,
                sendAt: sendAt,
                sendFailure: SafeReason(ex));
            try
            {
                await _notifications.AddAsync(failed, cancellationToken);
            }
            catch (Exception persistEx)
            {
                _logger.LogWarning("Failed to persist a send-failure record for order {OrderId}: {Reason}", orderId, SafeReason(persistEx));
            }
        }
    }

    public async Task RefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            return;
        }

        try
        {
            var current = await _messaging.FetchAsync(notification.ProviderSid, cancellationToken);
            var body = notification.ContentDisposed ? notification.Body : current.Body;
            notification.ApplyProviderState(current.Sid, current.Status, current.ErrorCode, current.ErrorMessage, body);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh notification {NotificationId}: {Reason}", notification.Id, SafeReason(ex));
        }
    }

    public async Task TryCancelPendingFollowUpAsync(int orderId, CancellationToken cancellationToken)
    {
        var records = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var record in records)
        {
            if (record.Kind != NotificationKinds.DeliveryFollowUp)
            {
                continue;
            }

            await RefreshAsync(record, cancellationToken);
            if (!record.IsStillPendingSend() || string.IsNullOrEmpty(record.ProviderSid))
            {
                continue;
            }

            try
            {
                var cancelled = await _messaging.CancelScheduledAsync(record.ProviderSid, cancellationToken);
                record.ApplyProviderState(cancelled.Sid, cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, record.Body);
                await _notifications.UpdateAsync(record, cancellationToken);
                _logger.LogInformation("Cancelled pending follow-up {NotificationId} for order {OrderId}.", record.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel follow-up {NotificationId} for order {OrderId}: {Reason}", record.Id, orderId, SafeReason(ex));
            }
        }
    }

    private static string SafeReason(Exception ex) =>
        ex is Exceptions.MessagingProviderException mpe
            ? mpe.Message
            : "The messaging provider call did not complete.";
}
