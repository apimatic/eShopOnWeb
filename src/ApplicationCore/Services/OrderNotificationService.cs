using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly ITwilioMessagingClient _twilio;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly ITwilioSettings _twilioSettings;

    public OrderNotificationService(
        ITwilioMessagingClient twilio,
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IAppLogger<OrderNotificationService> logger,
        ITwilioSettings twilioSettings)
    {
        _twilio = twilio;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _logger = logger;
        _twilioSettings = twilioSettings;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
        => TryNotifyAsync(orderId, buyerId, NotificationKind.OrderPlaced,
            $"Your eShop order #{orderId} has been placed. Thank you for shopping with us.",
            sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await TryNotifyAsync(orderId, buyerId, NotificationKind.OrderDispatched,
            $"Your eShop order #{orderId} is on its way.",
            sendAt: null, cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await TryNotifyAsync(orderId, buyerId, NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{orderId} go?",
            sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await CancelOutstandingFollowUpsAsync(orderId, cancellationToken);

        await TryNotifyAsync(orderId, buyerId, NotificationKind.OrderCancelled,
            $"Your eShop order #{orderId} has been cancelled.",
            sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshFromProviderAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpec(orderIds), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshFromProviderAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new NotificationActionException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpec(idempotencyKey.Trim()), cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("Returning existing resend notification {NotificationId} for a repeated idempotency key.", existing.Id);
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new NotificationActionException("Notification not found.");
        }

        if (original.ContentRedacted)
        {
            throw new NotificationActionException("The message content has been disposed of and cannot be re-sent.");
        }

        if (!string.IsNullOrWhiteSpace(original.ProviderMessageSid))
        {
            var latest = await _twilio.FetchAsync(original.ProviderMessageSid, cancellationToken);
            if (latest != null)
            {
                original.ApplyProviderSnapshot(latest.Status, latest.ErrorCode, latest.Body);
                await _notifications.UpdateAsync(original, cancellationToken);
            }
        }

        if (!original.DidNotReachShopper() && !string.IsNullOrWhiteSpace(original.ProviderMessageSid))
        {
            throw new NotificationActionException("Only messages that did not reach the shopper can be re-sent.");
        }

        var destination = await ResolveActiveDestinationAsync(original.BuyerId, original.ContactNumberId, cancellationToken);
        if (destination == null)
        {
            throw new NotificationActionException("The destination number is no longer on file; nothing will be sent to it again.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            original.Body,
            destination.Id,
            idempotencyKey.Trim(),
            original.Id);

        await _notifications.AddAsync(resend, cancellationToken);

        var result = await SafeSendAsync(destination.PhoneNumber, resend.Body, sendAt: null, cancellationToken);
        ApplySendResult(resend, result);
        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new NotificationActionException("Notification not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var redacted = await _twilio.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (redacted != null)
            {
                notification.ApplyProviderSnapshot(redacted.Status, redacted.ErrorCode, redacted.Body);
            }
        }

        notification.RedactLocalContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new NotificationActionException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _twilio.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new OrderNotificationsInCreatedRangeSpec(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages.Where(m => !string.IsNullOrWhiteSpace(m.Sid)))
        {
            if (localBySid.TryGetValue(provider.Sid!, out var notification))
            {
                matched.Add(ToEntry(notification, provider));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = provider.Sid,
                    ProviderStatus = provider.Status,
                    ProviderDateSent = provider.DateSent
                });
            }
        }

        foreach (var notification in local)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid)
                || !providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                eShopOnly.Add(ToEntry(notification, provider: null));
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _twilioSettings.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eShopOnly
        };
    }

    private async Task TryNotifyAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await ResolveActiveDestinationAsync(buyerId, contactNumberId: null, cancellationToken);
            if (destination == null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; no contact number on file.", kind, orderId);
                return;
            }

            var notification = new OrderNotification(orderId, buyerId, kind, body, destination.Id, scheduledSendAt: sendAt);
            await _notifications.AddAsync(notification, cancellationToken);

            var result = await SafeSendAsync(destination.PhoneNumber, body, sendAt, cancellationToken);
            ApplySendResult(notification, result);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notification {Kind} for order {OrderId} failed; the order operation still succeeds.", kind, orderId);
        }
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderIdSpec(orderId), cancellationToken);
            foreach (var followUp in followUps)
            {
                if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
                {
                    continue;
                }

                var latest = await _twilio.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                if (latest != null)
                {
                    followUp.ApplyProviderSnapshot(latest.Status, latest.ErrorCode, latest.Body);
                }

                if (string.Equals(followUp.ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(followUp.ProviderStatus, "cancelled", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(followUp.ProviderStatus, "sent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(followUp.ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(followUp.ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(followUp.ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    continue;
                }

                var cancelled = await _twilio.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                if (cancelled != null)
                {
                    followUp.ApplyProviderSnapshot(cancelled.Status, cancelled.ErrorCode, cancelled.Body);
                }

                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel scheduled follow-ups for order {OrderId}; the cancel operation still succeeds.", orderId);
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var latest = await _twilio.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (latest == null)
                {
                    continue;
                }

                notification.ApplyProviderSnapshot(latest.Status, latest.ErrorCode, latest.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private async Task<ContactNumber?> ResolveActiveDestinationAsync(string buyerId, int? contactNumberId, CancellationToken cancellationToken)
    {
        if (contactNumberId is int id)
        {
            return await _contactNumbers.FirstOrDefaultAsync(new ContactNumberByBuyerAndIdSpec(buyerId, id), cancellationToken);
        }

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<ProviderMessageResult> SafeSendAsync(string toE164, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            return await _twilio.SendAsync(toE164, body, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider send failed.");
            return new ProviderMessageResult { Accepted = false, Status = "failed" };
        }
    }

    private static void ApplySendResult(OrderNotification notification, ProviderMessageResult result)
    {
        if (result.Accepted && !string.IsNullOrWhiteSpace(result.Sid))
        {
            notification.RecordProviderAcceptance(result.Sid, string.IsNullOrWhiteSpace(result.Status) ? "queued" : result.Status);
            return;
        }

        notification.RecordProviderFailure(result.ErrorCode);
    }

    private static ReconciliationEntry ToEntry(OrderNotification notification, ProviderMessageResult? provider)
    {
        return new ReconciliationEntry
        {
            ProviderMessageSid = notification.ProviderMessageSid ?? provider?.Sid,
            NotificationId = notification.Id,
            ProviderStatus = provider?.Status,
            EShopStatus = notification.ProviderStatus,
            ProviderDateSent = provider?.DateSent,
            EShopCreatedAt = notification.CreatedAt
        };
    }
}
