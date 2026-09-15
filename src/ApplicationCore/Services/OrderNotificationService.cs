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
    /// <summary>How long after dispatch the "how did the delivery go" follow-up is queued for.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingService _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        ITwilioMessagingService twilio,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        foreach (var number in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderPlaced, number, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        foreach (var number in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            // Tell the shopper it is on its way now...
            await SendAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderDispatched, number, cancellationToken);
            // ...and queue the delivery follow-up with the provider for a few days later.
            await ScheduleFollowUpAsync(order.Id, order.BuyerId, number, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any delivery follow-up that has not yet gone out before anything else, so a
        // cancelled order can never trigger a "how did the delivery go" message.
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsForOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            await CancelScheduledAsync(followUp, cancellationToken);
        }

        foreach (var number in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            await SendAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderCancelled, number, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var list = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(list, cancellationToken);
        return list;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(int[] orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Length == 0)
        {
            return new List<OrderNotification>();
        }

        var list = await _notifications.ListAsync(new OrderNotificationsForOrdersSpecification(orderIds), cancellationToken);
        await RefreshStatusesAsync(list, cancellationToken);
        return list;
    }

    public async Task<OrderNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        return await _notifications.GetByIdAsync(notificationId, cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the message already produced, without sending another.
        var alreadyProduced = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyProduced is not null)
        {
            return ResendResult.AlreadyProcessed(alreadyProduced);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendResult.NotFound();
        }

        // Recompose the text (the original's content may have been disposed of).
        var body = NotificationMessageComposer.Compose(original.Type, original.OrderId);
        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Type, original.ToPhoneNumber, body);
        resend.SetIdempotencyKey(idempotencyKey);

        try
        {
            var message = await _twilio.SendAsync(original.ToPhoneNumber, body, cancellationToken);
            resend.RecordProviderMessage(message.Sid, message.Status, message.ErrorCode);
        }
        catch (Exception ex)
        {
            resend.RecordSendError();
            _logger.LogWarning("Re-send for order {OrderId} could not be delivered to the provider ({Error}).",
                original.OrderId, ex.GetType().Name);
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return ResendResult.Sent(resend);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the text at the provider first so it is no longer retrievable there; only then
        // mark it disposed locally. If the provider call fails we do not claim the content is gone.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _twilio.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of the content of notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for the configured sending number's messages in the range.
        var providerMessages = await _twilio.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var localMessages = await _notifications.ListAsync(new OrderNotificationsInDateRangeSpecification(from, to), cancellationToken);

        var providerBySid = new Dictionary<string, ProviderMessage>();
        foreach (var message in providerMessages)
        {
            providerBySid[message.Sid] = message;
        }

        var localSids = new HashSet<string>(
            localMessages.Where(l => !string.IsNullOrEmpty(l.ProviderMessageSid)).Select(l => l.ProviderMessageSid!));

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<OrderNotification>();
        foreach (var local in localMessages)
        {
            if (!string.IsNullOrEmpty(local.ProviderMessageSid) && providerBySid.TryGetValue(local.ProviderMessageSid!, out var provider))
            {
                matched.Add(new ReconciliationMatch(local, provider));
            }
            else
            {
                // eShop believes it sent (or tried) this, but the provider's ranged record does not show it.
                eShopOnly.Add(local);
            }
        }

        // Messages the provider knows about that eShop has no record of.
        var providerOnly = providerMessages.Where(m => !localSids.Contains(m.Sid)).ToList();

        return new ReconciliationReport(from, to, _twilio.ConfiguredFromNumber, matched, providerOnly, eShopOnly);
    }

    public async Task CancelScheduledMessagesToNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsForBuyerNumberSpecification(buyerId, phoneNumber), cancellationToken);
        foreach (var message in scheduled)
        {
            await CancelScheduledAsync(message, cancellationToken);
        }
    }

    // ----- helpers -----

    private async Task<IReadOnlyList<string>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        // A shopper with no number on file is simply not messaged.
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task SendAndRecordAsync(int orderId, string buyerId, NotificationType type, string toPhoneNumber, CancellationToken cancellationToken)
    {
        var body = NotificationMessageComposer.Compose(type, orderId);
        var notification = new OrderNotification(orderId, buyerId, type, toPhoneNumber, body);

        try
        {
            var message = await _twilio.SendAsync(toPhoneNumber, body, cancellationToken);
            notification.RecordProviderMessage(message.Sid, message.Status, message.ErrorCode);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.RecordSendError();
            _logger.LogWarning("Could not send {Type} notification for order {OrderId} ({Error}).",
                type, orderId, ex.GetType().Name);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(int orderId, string buyerId, string toPhoneNumber, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var body = NotificationMessageComposer.Compose(NotificationType.DeliveryFollowUp, orderId);
        var notification = new OrderNotification(orderId, buyerId, NotificationType.DeliveryFollowUp, toPhoneNumber, body);
        notification.SetScheduledFor(sendAt);

        try
        {
            var message = await _twilio.ScheduleAsync(toPhoneNumber, body, sendAt, cancellationToken);
            notification.RecordProviderMessage(message.Sid, message.Status, message.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.RecordSendError();
            _logger.LogWarning("Could not queue the delivery follow-up for order {OrderId} ({Error}).",
                orderId, ex.GetType().Name);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
        {
            return;
        }

        try
        {
            await _twilio.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
            followUp.UpdateDeliveryStatus(MessageDeliveryStatus.Canceled, null);
            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not call off the scheduled follow-up for order {OrderId} ({Error}).",
                followUp.OrderId, ex.GetType().Name);
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        // No public URL exists for the provider to call back into, so the current delivery outcome
        // has to be pulled from the provider on read for any message not yet in a terminal state.
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || MessageDeliveryStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var current = await _twilio.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (current is not null &&
                    (current.Status != notification.Status || (current.ErrorCode ?? "") != (notification.ErrorCode ?? "")))
                {
                    notification.UpdateDeliveryStatus(current.Status, current.ErrorCode);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery status for notification {NotificationId} ({Error}).",
                    notification.Id, ex.GetType().Name);
            }
        }
    }
}
