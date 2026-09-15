using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends the SMS messages that go out as an order moves and gives the operator the levers over them.
/// A message that cannot be sent is recorded but never fails the underlying order operation; a shopper
/// with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did delivery go?" follow-up is queued for.</summary>
    private const int FollowUpDelayDays = 3;

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var target = await GetTargetNumberAsync(order.BuyerId, cancellationToken);
        if (target is null)
        {
            _logger.LogInformation("Order {OrderId} placed but the shopper has no number on file; not messaging.", order.Id);
            return;
        }

        await RecordAndSendAsync(order, NotificationKind.OrderPlaced, BuildBody(NotificationKind.OrderPlaced, order.Id), target, scheduleAt: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var target = await GetTargetNumberAsync(order.BuyerId, cancellationToken);
        if (target is null)
        {
            _logger.LogInformation("Order {OrderId} dispatched but the shopper has no number on file; not messaging.", order.Id);
            return;
        }

        // Tell them it is on its way now...
        await RecordAndSendAsync(order, NotificationKind.OrderDispatched, BuildBody(NotificationKind.OrderDispatched, order.Id), target, scheduleAt: null, cancellationToken);

        // ...and queue the "how did delivery go?" follow-up with the provider for a few days later.
        var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);
        await RecordAndSendAsync(order, NotificationKind.DeliveryFollowUp, BuildBody(NotificationKind.DeliveryFollowUp, order.Id), target, scheduleAt: sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // First, call off any delivery follow-up that has not yet gone out: asking how a delivery went
        // for a cancelled order is exactly the incident this prevents.
        var existing = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in existing.Where(n => n.Kind == NotificationKind.DeliveryFollowUp && n.IsPendingScheduled))
        {
            try
            {
                await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateStatus(NotificationStatus.Canceled, null, null);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled the scheduled delivery follow-up {NotificationId} for order {OrderId}.", followUp.Id, order.Id);
            }
            catch (Exception ex)
            {
                // Cancelling the follow-up must not fail the order cancellation itself.
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId} (code {Code}).",
                    followUp.Id, order.Id, (ex as SmsProviderException)?.ProviderErrorCode ?? -1);
            }
        }

        // Then tell the shopper the order was cancelled.
        var target = await GetTargetNumberAsync(order.BuyerId, cancellationToken);
        if (target is null)
        {
            _logger.LogInformation("Order {OrderId} cancelled but the shopper has no number on file; not messaging.", order.Id);
            return;
        }

        await RecordAndSendAsync(order, NotificationKind.OrderCancelled, BuildBody(NotificationKind.OrderCancelled, order.Id), target, scheduleAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> GetForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, IReadOnlyList<OrderNotification>>();

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrdersSpecification(ids), cancellationToken);
        await RefreshOutcomesAsync(notifications, cancellationToken);

        return notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new BadRequestException("An idempotency key is required to re-send a message.");

        // Idempotency: if this key has already produced a message, return it and send nothing more.
        var alreadyDone = await _notifications.FirstOrDefaultAsync(new OrderNotificationByResendKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            return new ResendResult { Status = ResendStatus.Duplicate, Notification = alreadyDone };
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult { Status = ResendStatus.OriginalNotFound };
        }

        // Make sure we are acting on the latest known outcome before deciding.
        await RefreshOutcomesAsync(new[] { original }, cancellationToken);
        if (NotificationStatus.IsDelivered(original.ProviderStatus))
        {
            return new ResendResult { Status = ResendStatus.AlreadyDelivered, Notification = original };
        }

        var resent = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToNumber);
        resent.SetResendMetadata(idempotencyKey, original.Id);

        try
        {
            var result = await _smsProvider.SendAsync(original.ToNumber, BuildBody(original.Kind, original.OrderId), cancellationToken);
            resent.RecordAccepted(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage, null);
        }
        catch (Exception ex)
        {
            resent.RecordSendError((ex as SmsProviderException)?.ProviderErrorCode, "Message could not be sent.");
            _logger.LogWarning("Re-send for order {OrderId} was not accepted by the provider (code {Code}).",
                original.OrderId, (ex as SmsProviderException)?.ProviderErrorCode ?? -1);
        }

        // The record (with its idempotency key) is stored whether or not the send was accepted, so a
        // repeat under the same key never sends again; a genuine retry uses a fresh key.
        await _notifications.AddAsync(resent, cancellationToken);
        return new ResendResult { Status = ResendStatus.Sent, Notification = resent };
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            // Redact at the provider so the text is no longer retrievable there. If this fails it
            // surfaces to the caller (the disposal did not actually happen).
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of the content of notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new BadRequestException("'to' must be on or after 'from'.");

        // Ask the provider for its own record of this sender's messages in the range...
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);
        // ...and line them up against what eShop believes it sent in the same range.
        var eShopNotifications = await _notifications.ListAsync(new OrderNotificationsSentInRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .GroupBy(m => m.MessageSid)
            .ToDictionary(g => g.Key, g => g.First());

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, providerMsg) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notif))
            {
                matched.Add(new ReconciliationEntry
                {
                    MessageSid = sid,
                    InProvider = true,
                    InEShop = true,
                    ProviderStatus = providerMsg.Status,
                    EShopStatus = notif.ProviderStatus,
                    ProviderDateSent = providerMsg.DateSent,
                    NotificationId = notif.Id,
                    OrderId = notif.OrderId
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    MessageSid = sid,
                    InProvider = true,
                    InEShop = false,
                    ProviderStatus = providerMsg.Status,
                    ProviderDateSent = providerMsg.DateSent
                });
            }
        }

        foreach (var (sid, notif) in eShopBySid)
        {
            if (providerBySid.ContainsKey(sid)) continue;
            eShopOnly.Add(new ReconciliationEntry
            {
                MessageSid = sid,
                InProvider = false,
                InEShop = true,
                EShopStatus = notif.ProviderStatus,
                NotificationId = notif.Id,
                OrderId = notif.OrderId
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderCount = providerBySid.Count,
            EShopCount = eShopBySid.Count,
            MatchedCount = matched.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    /// <summary>The number to reach the shopper on: their most recently registered number, if any.</summary>
    private async Task<ContactNumber?> GetTargetNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault(); // spec orders newest first
    }

    private async Task RecordAndSendAsync(Order order, NotificationKind kind, string body, ContactNumber target, DateTimeOffset? scheduleAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, target.PhoneNumber);
        try
        {
            var result = scheduleAt is null
                ? await _smsProvider.SendAsync(target.PhoneNumber, body, cancellationToken)
                : await _smsProvider.ScheduleAsync(target.PhoneNumber, body, scheduleAt.Value, cancellationToken);

            notification.RecordAccepted(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage, result.ScheduledSendAt);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the order operation.
            notification.RecordSendError((ex as SmsProviderException)?.ProviderErrorCode, "Message could not be sent.");
            _logger.LogWarning("Order {OrderId} {Kind} message was not accepted by the provider (code {Code}).",
                order.Id, kind, (ex as SmsProviderException)?.ProviderErrorCode ?? -1);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Refresh the last-known delivery outcome of any non-terminal messages by reading the provider.
    /// There is no callback URL for this app, so state is obtained by asking the provider on demand.
    /// </summary>
    private async Task RefreshOutcomesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid)) continue;
            if (NotificationStatus.IsTerminal(notification.ProviderStatus)) continue;

            try
            {
                var status = await _smsProvider.GetStatusAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // Reporting must not fail just because a status read did; keep the last-known value.
                _logger.LogWarning("Could not refresh outcome for notification {NotificationId} (code {Code}).",
                    notification.Id, (ex as SmsProviderException)?.ProviderErrorCode ?? -1);
            }
        }
    }

    private static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: your order #{orderId} has been placed. Thanks for shopping with us!",
        NotificationKind.OrderDispatched => $"eShopOnWeb: good news - your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShopOnWeb: your order #{orderId} has been cancelled. Please contact us if this is unexpected.",
        _ => $"eShopOnWeb: an update about your order #{orderId}."
    };
}
