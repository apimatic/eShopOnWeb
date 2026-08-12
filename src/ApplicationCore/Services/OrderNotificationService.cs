using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How far after dispatch the "how did delivery go?" follow-up is scheduled. Well within the
    // provider's 15-minute .. 35-day scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        NotificationDeliveryStatus.Delivered,
        NotificationDeliveryStatus.Undelivered,
        NotificationDeliveryStatus.Failed,
        NotificationDeliveryStatus.Canceled,
        NotificationDeliveryStatus.SendFailed
    };

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => SendToEachContactNumberAsync(order, NotificationType.OrderPlaced, sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToEachContactNumberAsync(order, NotificationType.OrderDispatched, sendAt: null, cancellationToken);
        // Queue the follow-up with the provider itself, for a few days out.
        await SendToEachContactNumberAsync(order, NotificationType.DeliveryFollowUp, sendAt: DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any follow-up that has not yet gone out, before it can reach the shopper.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await SendToEachContactNumberAsync(order, NotificationType.OrderCancelled, sendAt: null, cancellationToken);
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the message the first call produced.
        var existing = (await _notificationRepository.ListAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken)).FirstOrDefault();
        if (existing is not null)
        {
            _logger.LogInformation("Resend under key already satisfied by notification {NotificationId}; not sending again.", existing.Id);
            return existing;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
            return null;

        // A fresh send to the same destination, carrying the idempotency key. Reuses the source's
        // logical type so the reconstructed body matches the original message.
        var resend = new OrderNotification(
            source.OrderId, source.BuyerId, source.Type, source.ToPhoneNumber, source.ContactNumberId, idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        var body = OrderNotificationMessages.Compose(source.Type, source.OrderId);
        await DispatchAsync(resend, body, sendAt: null, cancellationToken);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.ProviderMessageSid is null)
            return false;

        await _smsProvider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Content disposed for notification {NotificationId} (order {OrderId}).", notification.Id, notification.OrderId);
        return true;
    }

    public async Task RefreshDeliveryStatusesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null)
                continue;
            if (TerminalStatuses.Contains(notification.DeliveryStatus))
                continue;

            try
            {
                var status = await _smsProvider.FetchStatusAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // Refreshing status must never fail the caller's read.
                _logger.LogWarning("Could not refresh status for notification {NotificationId} (order {OrderId}): {Reason}",
                    notification.Id, notification.OrderId, Describe(ex));
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsProvider.ListOutboundMessagesAsync(from, to, cancellationToken);
        var eShopSent = await _notificationRepository.ListAsync(new OrderNotificationsSentInRangeSpecification(from, to), cancellationToken);

        var providerBySid = new Dictionary<string, ProviderMessage>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in providerMessages)
            providerBySid.TryAdd(message.Sid, message);

        var eShopBySid = new Dictionary<string, OrderNotification>(StringComparer.OrdinalIgnoreCase);
        foreach (var notification in eShopSent)
            if (notification.ProviderMessageSid is not null)
                eShopBySid.TryAdd(notification.ProviderMessageSid, notification);

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<ReconciliationEShopOnly>();
        foreach (var notification in eShopBySid.Values)
        {
            if (providerBySid.TryGetValue(notification.ProviderMessageSid!, out var providerMessage))
            {
                matched.Add(new ReconciliationMatch(
                    notification.ProviderMessageSid!, notification.Id, notification.OrderId,
                    notification.DeliveryStatus, providerMessage.Status, providerMessage.ErrorCode));
            }
            else
            {
                eShopOnly.Add(new ReconciliationEShopOnly(
                    notification.ProviderMessageSid!, notification.Id, notification.OrderId, notification.DeliveryStatus));
            }
        }

        var providerOnly = providerMessages
            .Where(m => !eShopBySid.ContainsKey(m.Sid))
            .Select(m => new ReconciliationProviderOnly(m.Sid, m.Status, m.ErrorCode, m.DateSent))
            .ToList();

        return new ReconciliationReport(
            from, to, _smsProvider.SenderNumber,
            providerMessages.Count, eShopBySid.Count,
            matched, providerOnly, eShopOnly);
    }

    private async Task SendToEachContactNumberAsync(Order order, NotificationType type, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("Order {OrderId}: shopper has no number on file; no {Type} message sent.", order.Id, type);
            return;
        }

        var body = OrderNotificationMessages.Compose(type, order.Id);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, type, number.PhoneNumber, number.Id);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            await DispatchAsync(notification, body, sendAt, cancellationToken);
        }
    }

    private async Task DispatchAsync(OrderNotification notification, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsProvider.SendAsync(notification.ToPhoneNumber, body, sendAt, cancellationToken);
            if (result.Accepted)
            {
                notification.RecordAccepted(result.MessageSid!, result.Status, result.ErrorCode, result.ErrorMessage, result.ScheduledSendAt);
                _logger.LogInformation("Order {OrderId} notification {NotificationId} ({Type}) accepted by provider with status {Status}.",
                    notification.OrderId, notification.Id, notification.Type, notification.DeliveryStatus);
            }
            else
            {
                notification.RecordSendFailed(result.ErrorMessage);
                _logger.LogWarning("Order {OrderId} notification {NotificationId} ({Type}) not accepted by provider (code {Code}).",
                    notification.OrderId, notification.Id, notification.Type, result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying order operation.
            notification.RecordSendFailed(Describe(ex));
            _logger.LogWarning("Order {OrderId} notification {NotificationId} ({Type}) could not be sent: {Reason}",
                notification.OrderId, notification.Id, notification.Type, Describe(ex));
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications.Where(n => n.Type == NotificationType.DeliveryFollowUp && n.IsScheduled()))
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.MarkCancelled();
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
                _logger.LogInformation("Follow-up notification {NotificationId} for order {OrderId} cancelled before send.",
                    notification.Id, orderId);
            }
            catch (Exception ex)
            {
                // Cancelling the follow-up must never fail the cancel operation itself.
                _logger.LogWarning("Could not cancel follow-up notification {NotificationId} for order {OrderId}: {Reason}",
                    notification.Id, orderId, Describe(ex));
            }
        }
    }

    // Exception text stripped to its type and any message we control; the SMS provider client is
    // responsible for keeping the shopper's number out of the messages it throws.
    private static string Describe(Exception ex) => ex.Message;
}
