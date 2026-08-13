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

public class NotificationService : INotificationService
{
    /// <summary>"A few days later" for the post-dispatch follow-up (within the provider's scheduling window).</summary>
    private const int FollowUpDelayDays = 3;

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<SmsNotification> _notifications;
    private readonly ISmsProvider _sms;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<SmsNotification> notifications,
        ISmsProvider sms,
        IAppLogger<NotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyNotifyAsync(order.Id, async numbers =>
        {
            foreach (var number in numbers)
                await SendImmediateAsync(order, NotificationKind.OrderPlaced, number, cancellationToken);
        }, order.BuyerId, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyNotifyAsync(order.Id, async numbers =>
        {
            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, NotificationKind.OrderDispatched, number, cancellationToken);
                await ScheduleFollowUpAsync(order, number, cancellationToken);
            }
        }, order.BuyerId, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Tell the shopper (best effort) ...
        await SafelyNotifyAsync(order.Id, async numbers =>
        {
            foreach (var number in numbers)
                await SendImmediateAsync(order, NotificationKind.OrderCancelled, number, cancellationToken);
        }, order.BuyerId, cancellationToken);

        // ... and call off any follow-up still queued for this order so it can never reach them.
        // This runs regardless of the shopper's current numbers (they may have removed one since dispatch).
        try
        {
            var forOrder = await _notifications.ListAsync(new OrderNotificationsSpecification(order.Id), cancellationToken);
            foreach (var pending in forOrder.Where(n => n.IsScheduledAndPending))
                await CancelScheduledAsync(pending, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed while cancelling scheduled follow-ups for order {0}: {1}", order.Id, ex.Message);
        }
    }

    public async Task CancelPendingForNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var mine = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
            foreach (var pending in mine.Where(n => n.IsScheduledAndPending && n.ToNumber == phoneNumber))
                await CancelScheduledAsync(pending, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never written to logs: the number. Only the buyer id.
            _logger.LogWarning("Failed while cancelling scheduled follow-ups for a removed number of buyer {0}: {1}", buyerId, ex.Message);
        }
    }

    public async Task<SmsNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotent replay: a request already handled under this key must not send a second message.
        var existing = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
            return existing;

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return null;

        var body = BuildBody(original.Kind, original.OrderId, original.MessageBody);
        var resend = SmsNotification.ForImmediateSend(original.OrderId, original.BuyerId, NotificationKind.Resend, original.ToNumber, body);
        resend.AssignIdempotencyKey(idempotencyKey);

        // Reserve the key before the network call so a concurrent duplicate is deduplicated.
        resend = await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _sms.SendAsync(original.ToNumber, body, cancellationToken);
            resend.SetProviderResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed();
            _logger.LogWarning("Re-send for notification {0} could not be handed to the provider: {1}", notificationId, ex.Message);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<SmsNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return null;

        // The text must no longer be retrievable from the provider either — not merely hidden here.
        // If the provider redaction fails, surface it (do not redact locally) so the state stays consistent.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
            await _sms.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task<IReadOnlyList<SmsNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var list = await _notifications.ListAsync(new OrderNotificationsSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(list, cancellationToken);
        return list;
    }

    public async Task RefreshStatusesAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || IsTerminal(notification.Status))
                continue;

            try
            {
                var state = await _sms.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (state is not null && !string.Equals(state.Status, notification.Status, StringComparison.OrdinalIgnoreCase))
                {
                    notification.UpdateStatus(state.Status, state.ErrorCode);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this app's own sending number's messages (filtered provider-side).
        var providerMessages = await _sms.ListSentMessagesAsync(from, to, cancellationToken);

        var eShopSent = await _notifications.ListAsync(new SentNotificationsSpecification(), cancellationToken);
        var eShopBySid = eShopSent
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var onlyAtProvider = new List<ReconciliationEntry>();
        foreach (var message in providerMessages)
        {
            if (eShopBySid.TryGetValue(message.Sid, out var n))
                matched.Add(ToEntry(message, n));
            else
                onlyAtProvider.Add(ToEntry(message, null));
        }

        // eShop believes it actually sent (within the range) but the provider's list does not include it.
        // A follow-up still scheduled, or one that was called off before it went out, was never sent —
        // exclude those so they do not read as discrepancies.
        var onlyInEShop = eShopSent
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to
                && !providerSids.Contains(n.ProviderMessageSid!)
                && n.Status != SmsNotification.StatusScheduled
                && n.Status != SmsNotification.StatusCanceled)
            .Select(n => ToEntry(null, n))
            .ToList();

        return new ReconciliationReport(from, to, _sms.FromNumber, matched, onlyAtProvider, onlyInEShop);
    }

    // -- helpers -------------------------------------------------------------

    private async Task SafelyNotifyAsync(int orderId, Func<IReadOnlyList<ContactNumber>, Task> action, string buyerId, CancellationToken cancellationToken)
    {
        // A message that cannot be sent must never fail the underlying order operation.
        try
        {
            var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
            if (numbers.Count == 0)
                return; // A shopper with no number on file is simply not messaged.

            await action(numbers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification handling for order {0} failed but the order operation stands: {1}", orderId, ex.Message);
        }
    }

    private async Task SendImmediateAsync(Order order, NotificationKind kind, ContactNumber number, CancellationToken cancellationToken)
    {
        var body = BuildBody(kind, order.Id, null);
        var notification = SmsNotification.ForImmediateSend(order.Id, order.BuyerId, kind, number.PhoneNumber, body);
        try
        {
            var result = await _sms.SendAsync(number.PhoneNumber, body, cancellationToken);
            notification.SetProviderResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("A {0} message for order {1} could not be handed to the provider: {2}", kind, order.Id, ex.Message);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, ContactNumber number, CancellationToken cancellationToken)
    {
        var body = BuildBody(NotificationKind.DeliveryFollowUp, order.Id, null);
        var notification = SmsNotification.ForImmediateSend(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
        var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);
        try
        {
            var result = await _sms.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
            notification.SetProviderResult(result.Sid, result.Status, result.ErrorCode, sendAt);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("The follow-up for order {0} could not be scheduled with the provider: {1}", order.Id, ex.Message);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledAsync(SmsNotification pending, CancellationToken cancellationToken)
    {
        try
        {
            await _sms.CancelScheduledAsync(pending.ProviderMessageSid!, cancellationToken);
            pending.MarkCanceled();
            await _notifications.UpdateAsync(pending, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not cancel scheduled follow-up notification {0}: {1}", pending.Id, ex.Message);
        }
    }

    private static bool IsTerminal(string status) =>
        status is SmsNotification.StatusDelivered
            or SmsNotification.StatusUndelivered
            or SmsNotification.StatusFailed
            or SmsNotification.StatusCanceled;

    private static string BuildBody(NotificationKind kind, int orderId, string? existingBody) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched => $"eShopOnWeb: good news — your order #{orderId} is on its way!",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCancelled => $"eShopOnWeb: your order #{orderId} has been cancelled. If this is unexpected, please contact support.",
        // Re-send of an earlier message: prefer its original text, otherwise a safe generic line.
        _ => !string.IsNullOrEmpty(existingBody)
            ? existingBody!
            : $"eShopOnWeb: an update about your order #{orderId}."
    };

    private static ReconciliationEntry ToEntry(ProviderMessage? provider, SmsNotification? eShop) => new(
        ProviderMessageSid: provider?.Sid ?? eShop?.ProviderMessageSid,
        ProviderStatus: provider?.Status,
        ProviderErrorCode: provider?.ErrorCode,
        DateSent: provider?.DateSent,
        NotificationId: eShop?.Id,
        OrderId: eShop?.OrderId,
        EShopStatus: eShop?.Status);
}
