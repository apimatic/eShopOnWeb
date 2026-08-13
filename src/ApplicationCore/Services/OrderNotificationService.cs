using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far in the future the "how did delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ISmsNotificationProvider _provider;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IReadRepository<Order> _orders;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsNotificationProvider provider,
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        IReadRepository<Order> orders,
        IAppLogger<OrderNotificationService> logger)
    {
        _provider = provider;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orders = orders;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderPlaced,
                ComposeBody(NotificationType.OrderPlaced, order.Id), cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderDispatched,
                ComposeBody(NotificationType.OrderDispatched, order.Id), cancellationToken);

            // Queue the "how did delivery go?" follow-up WITH THE PROVIDER for a few days later — the
            // provider holds and sends it, not this application.
            await ScheduleFollowUpAsync(order, number.PhoneNumber, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderCancelled,
                ComposeBody(NotificationType.OrderCancelled, order.Id), cancellationToken);
        }

        // A follow-up that has not yet gone out must never reach the customer for a cancelled order.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Repeating a request under the same key must not send a second message: if a resend already
        // happened under this key, return that record without sending again.
        var alreadyResent = await _notifications.FirstOrDefaultAsync(
            new NotificationResendSpecification(notificationId, idempotencyKey), cancellationToken);
        if (alreadyResent is not null)
        {
            _logger.LogInformation("Resend for notification {0} under an existing idempotency key returned the prior message {1}.",
                notificationId, alreadyResent.Id);
            return alreadyResent;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (source.ContentDisposed || string.IsNullOrEmpty(source.Body))
        {
            throw new InvalidNotificationOperationException(
                "The message content has been disposed of and can no longer be re-sent.");
        }

        // A number the shopper has removed must never be sent to again.
        var stillRegistered = await _contactNumbers.AnyAsync(
            new ContactNumberByBuyerAndNumberSpecification(source.BuyerId, source.ToPhoneNumber), cancellationToken);
        if (!stillRegistered)
        {
            throw new InvalidNotificationOperationException(
                "The destination number is no longer registered; nothing can be sent to it.");
        }

        // A genuine second attempt under a fresh key is legitimate — send and record it.
        var resent = new OrderNotification(source.OrderId, source.BuyerId, source.Type, source.ToPhoneNumber, source.Body);
        resent.MarkAsResendOf(notificationId, idempotencyKey);

        var message = await _provider.SendAsync(source.ToPhoneNumber, source.Body, cancellationToken);
        resent.MarkAccepted(message.Sid, message.Status, message.ErrorCode);

        var saved = await _notifications.AddAsync(resent, cancellationToken);
        _logger.LogInformation("Re-sent notification {0} for order {1} as message {2}.",
            notificationId, source.OrderId, saved.Id);
        return saved;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Remove the text from the provider too — not merely hide it here — while the fact a message
        // was sent and what became of it survive. If the provider cannot redact, surface the failure
        // rather than reporting a disposal that did not fully happen.
        if (!notification.ContentDisposed && !string.IsNullOrEmpty(notification.MessageSid))
        {
            await _provider.RedactContentAsync(notification.MessageSid!, cancellationToken);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {0} (order {1}).",
            notification.Id, notification.OrderId);
        return true;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scope to the caller's own order — one shopper must never see another's.
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        return await RefreshNotificationsForOrderAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> RefreshNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshDeliveryStateAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<ReconciliationReport> BuildReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only about eShop's own configured sending number's messages over the range.
        var providerMessages = await _provider.ListSentMessagesAsync(from, to, cancellationToken);

        // What eShop believes it sent over the same range (records that carry a provider message SID).
        var localNotifications = await _notifications.ListAsync(
            new NotificationsWithSidInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<ReconciliationEShopOnly>();
        foreach (var (sid, notification) in localBySid)
        {
            if (providerBySid.TryGetValue(sid, out var providerMessage))
            {
                matched.Add(new ReconciliationMatch
                {
                    MessageSid = sid,
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    ProviderStatus = providerMessage.Status,
                    EShopStatus = notification.DeliveryStatus,
                    StatusesAgree = string.Equals(providerMessage.Status, notification.DeliveryStatus, StringComparison.OrdinalIgnoreCase)
                });
            }
            else
            {
                eShopOnly.Add(new ReconciliationEShopOnly
                {
                    MessageSid = sid,
                    NotificationId = notification.Id,
                    OrderId = notification.OrderId,
                    EShopStatus = notification.DeliveryStatus
                });
            }
        }

        var providerOnly = providerBySid
            .Where(kvp => !localBySid.ContainsKey(kvp.Key))
            .Select(kvp => new ReconciliationProviderOnly
            {
                MessageSid = kvp.Key,
                ProviderStatus = kvp.Value.Status,
                DateSent = kvp.Value.DateSent,
                MaskedTo = MaskNumber(kvp.Value.To)
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly,
            ProviderMessageCount = providerBySid.Count,
            EShopMessageCount = localBySid.Count
        };
    }

    private async Task<IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        // A shopper with no number on file is simply not messaged.
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    private async Task SendImmediateAsync(Order order, string toPhoneNumber, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toPhoneNumber, body);
        try
        {
            var message = await _provider.SendAsync(toPhoneNumber, body, cancellationToken);
            notification.MarkAccepted(message.Sid, message.Status, message.ErrorCode);
        }
        catch (NotificationProviderException ex)
        {
            // A message that cannot be sent must NEVER fail the underlying operation.
            notification.MarkSendFailed();
            _logger.LogWarning("Could not send a {0} message for order {1}: {2}", type, order.Id, ex.Message);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toPhoneNumber, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = ComposeBody(NotificationType.DeliveryFollowUp, order.Id);
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp,
            toPhoneNumber, body, isFollowUp: true, scheduledSendAt: sendAt);
        try
        {
            var message = await _provider.ScheduleAsync(toPhoneNumber, body, sendAt, cancellationToken);
            notification.MarkAccepted(message.Sid, message.Status ?? OrderNotification.StatusScheduled, message.ErrorCode);
        }
        catch (NotificationProviderException ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("Could not schedule the delivery follow-up for order {0}: {1}", order.Id, ex.Message);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.IsFollowUp && !n.IsTerminal() && !string.IsNullOrEmpty(n.MessageSid)))
        {
            try
            {
                var message = await _provider.CancelScheduledAsync(followUp.MessageSid!, cancellationToken);
                followUp.MarkCancelled(message.Status);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off the queued follow-up (notification {0}) for cancelled order {1}.",
                    followUp.Id, orderId);
            }
            catch (NotificationProviderException ex)
            {
                // Cancelling the follow-up must not fail the order cancellation itself.
                _logger.LogWarning("Could not call off the follow-up (notification {0}) for order {1}: {2}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task RefreshDeliveryStateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        // The provider owns the delivery outcome and there is no callback into this app, so re-query it
        // for messages that are still in flight. Settled outcomes are left as-is.
        if (string.IsNullOrEmpty(notification.MessageSid) || notification.IsTerminal())
        {
            return;
        }

        try
        {
            var message = await _provider.GetMessageAsync(notification.MessageSid!, cancellationToken);
            notification.RefreshDeliveryState(message.Status, message.ErrorCode);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (NotificationProviderException ex)
        {
            // Reporting must not fail because a status refresh could not be obtained — keep last known.
            _logger.LogWarning("Could not refresh delivery state for notification {0}: {1}", notification.Id, ex.Message);
        }
    }

    private static string ComposeBody(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationType.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way.",
        NotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. Contact us if this is unexpected.",
        NotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        _ => $"eShop: an update about your order #{orderId}."
    };

    private static string? MaskNumber(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }

        var lastFour = number.Length <= 4 ? number : number.Substring(number.Length - 4);
        return "****" + lastFour;
    }
}
