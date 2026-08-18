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
    private readonly ISmsGateway _gateway;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly MessagingSettings _settings;
    private readonly IAppLogger<NotificationService> _logger;

    // Delivery outcomes that will not change again — no point re-reading the provider for these.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read",
        Notification.StatusSendFailed
    };

    public NotificationService(
        ISmsGateway gateway,
        IRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        MessagingSettings settings,
        IAppLogger<NotificationService> logger)
    {
        _gateway = gateway;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _settings = settings;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        var number = await ResolveShopperNumberAsync(order.BuyerId, cancellationToken);
        if (number is null)
        {
            LogNoNumber(order.Id);
            return;
        }

        var body = $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us!";
        await SendImmediateAndRecordAsync(order, NotificationKind.OrderPlaced, body, number, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        var number = await ResolveShopperNumberAsync(order.BuyerId, cancellationToken);
        if (number is null)
        {
            LogNoNumber(order.Id);
            return;
        }

        var dispatchBody = $"Good news! Your eShop order #{order.Id} is on its way.";
        await SendImmediateAndRecordAsync(order, NotificationKind.OrderDispatched, dispatchBody, number, cancellationToken);

        // Queue a "how did delivery go?" follow-up WITH THE PROVIDER for a few days later. The
        // provider holds and sends it; this app runs no timer.
        var sendAt = DateTimeOffset.UtcNow.Add(_settings.FollowUpDelay);
        var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.";
        var followUp = new Notification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.CanonicalNumber, followUpBody);
        try
        {
            var result = await _gateway.ScheduleAsync(number.CanonicalNumber, followUpBody, sendAt, cancellationToken);
            if (result.Accepted && !string.IsNullOrEmpty(result.MessageSid))
                followUp.MarkScheduled(result.MessageSid!, result.Status ?? "scheduled", sendAt);
            else
                followUp.RecordSendFailed(result.ErrorMessage ?? "provider did not accept the scheduled message");
        }
        catch (Exception ex)
        {
            // A messaging failure must never fail the dispatch.
            _logger.LogWarning("Failed to schedule delivery follow-up for order {OrderId}: {Error}", order.Id, ex.Message);
            followUp.RecordSendFailed("scheduling failed");
        }
        await _notifications.AddAsync(followUp, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        // Tell the shopper (if we have a number on file).
        var number = await ResolveShopperNumberAsync(order.BuyerId, cancellationToken);
        if (number is not null)
        {
            var body = $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
            await SendImmediateAndRecordAsync(order, NotificationKind.OrderCancelled, body, number, cancellationToken);
        }
        else
        {
            LogNoNumber(order.Id);
        }

        // Call off any not-yet-sent follow-up for this order so a "how did delivery go?" message can
        // never reach the shopper for a cancelled order. This runs regardless of whether a current
        // number is on file — the follow-up was queued earlier.
        var existing = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in existing.Where(n => n.IsPendingScheduled))
        {
            try
            {
                var cancelled = await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (cancelled)
                {
                    followUp.UpdateDeliveryStatus("canceled", null, null);
                    _logger.LogInformation("Cancelled scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, order.Id);
                }
                else
                {
                    _logger.LogWarning("Provider did not confirm cancellation of follow-up {NotificationId} for order {OrderId}.", followUp.Id, order.Id);
                }
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                // Never let this fail the cancel operation.
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}", followUp.Id, order.Id, ex.Message);
            }
        }
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken)
    {
        var list = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        if (!refreshFromProvider)
            return list;

        foreach (var n in list)
        {
            if (string.IsNullOrEmpty(n.ProviderMessageSid) || n.ContentDisposed)
                continue;
            if (TerminalStatuses.Contains(n.DeliveryStatus))
                continue;

            try
            {
                var fresh = await _gateway.FetchAsync(n.ProviderMessageSid!, cancellationToken);
                if (fresh.Accepted && !string.IsNullOrEmpty(fresh.Status))
                {
                    n.UpdateDeliveryStatus(fresh.Status!, fresh.ErrorCode, fresh.ErrorMessage);
                    await _notifications.UpdateAsync(n, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // A read failure on one message must not fail the whole listing.
                _logger.LogWarning("Failed to refresh status for notification {NotificationId}: {Error}", n.Id, ex.Message);
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<Notification>> GetNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
            return Array.Empty<Notification>();
        return await _notifications.ListAsync(new NotificationsByOrdersSpecification(orderIds), cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return new ResendResult(ResendOutcome.OriginalNotFound, null);

        // Idempotency: a repeat under the same key returns the message the first attempt produced,
        // without sending a second one.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (priorForKey is not null)
            return new ResendResult(ResendOutcome.ReplayedIdempotent, priorForKey);

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
            return new ResendResult(ResendOutcome.ContentDisposed, null);

        var resend = new Notification(original.OrderId, original.OwnerId, NotificationKind.Resend, original.ToNumber, original.Body!);
        resend.SetIdempotencyKey(idempotencyKey);
        try
        {
            var result = await _gateway.SendAsync(original.ToNumber, original.Body!, cancellationToken);
            if (result.Accepted && !string.IsNullOrEmpty(result.MessageSid))
                resend.RecordAccepted(result.MessageSid!, result.Status ?? string.Empty, result.ErrorCode, result.ErrorMessage);
            else
                resend.RecordSendFailed(result.ErrorMessage ?? "provider did not accept the message");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} failed: {Error}", notificationId, ex.Message);
            resend.RecordSendFailed("send failed");
        }
        await _notifications.AddAsync(resend, cancellationToken);
        return new ResendResult(ResendOutcome.Created, resend);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        if (notification.ContentDisposed)
            return true; // already disposed — idempotent

        // Dispose of the text at the provider first, so it is no longer retrievable there. If the
        // provider never accepted the message (no SID), there is nothing to redact upstream.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _gateway.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // Ask the provider for the messages IT knows about that were sent from our own number.
        var providerMessages = await _gateway.ListSentMessagesAsync(from, to, cancellationToken);

        // What eShop believes it sent in the same window.
        var eShopNotifications = await _notifications.ListAsync(
            new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var msg in providerMessages)
        {
            if (eShopBySid.TryGetValue(msg.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(msg.Sid, n.Id, n.OrderId, n.DeliveryStatus, msg.Status));
            }
            else
            {
                // The provider knows about this message but eShop has no record of it.
                providerOnly.Add(new ReconciliationEntry(msg.Sid, null, null, null, msg.Status));
            }
        }

        foreach (var n in eShopNotifications)
        {
            // eShop believes it sent this, but the provider's range does not contain it (either it
            // never reached the provider — no SID — or the SID is absent from the provider's answer).
            var hasSid = !string.IsNullOrEmpty(n.ProviderMessageSid);
            if (hasSid && providerBySid.ContainsKey(n.ProviderMessageSid!))
                continue;
            eShopOnly.Add(new ReconciliationEntry(n.ProviderMessageSid, n.Id, n.OrderId, n.DeliveryStatus, null));
        }

        return new ReconciliationReport(from, to, _settings.FromNumber, matched, providerOnly, eShopOnly);
    }

    private async Task<ContactNumber?> ResolveShopperNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        // Send to the shopper's most recently registered number (the spec orders newest first).
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<Notification> SendImmediateAndRecordAsync(Order order, NotificationKind kind, string body, ContactNumber number, CancellationToken cancellationToken)
    {
        var notification = new Notification(order.Id, order.BuyerId, kind, number.CanonicalNumber, body);
        try
        {
            var result = await _gateway.SendAsync(number.CanonicalNumber, body, cancellationToken);
            if (result.Accepted && !string.IsNullOrEmpty(result.MessageSid))
                notification.RecordAccepted(result.MessageSid!, result.Status ?? string.Empty, result.ErrorCode, result.ErrorMessage);
            else
                notification.RecordSendFailed(result.ErrorMessage ?? "provider did not accept the message");
        }
        catch (Exception ex)
        {
            // A messaging failure must never fail the underlying order operation.
            _logger.LogWarning("Failed to send {Kind} message for order {OrderId}: {Error}", kind, order.Id, ex.Message);
            notification.RecordSendFailed("send failed");
        }
        await _notifications.AddAsync(notification, cancellationToken);
        return notification;
    }

    private void LogNoNumber(int orderId)
        => _logger.LogInformation("No contact number on file for order {OrderId}; shopper not messaged.", orderId);
}
