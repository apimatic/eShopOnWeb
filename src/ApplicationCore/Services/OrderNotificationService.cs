using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<NotificationResendRecord> _resendRecords;
    private readonly ITwilioGateway _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IRepository<NotificationResendRecord> resendRecords,
        ITwilioGateway twilio,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _resendRecords = resendRecords;
        _twilio = twilio;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => SendKindAsync(order, NotificationKind.OrderPlaced, sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendKindAsync(order, NotificationKind.OrderDispatched, sendAt: null, cancellationToken);
        await SendKindAsync(order, NotificationKind.DeliveryFollowUp, DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await SendKindAsync(order, NotificationKind.OrderCancelled, sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var list = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(list, cancellationToken);
        return list;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var list = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshStatusesAsync(list, cancellationToken);
        return list;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return null;
        }

        var existingKey = await _resendRecords.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existingKey != null)
        {
            return await _notifications.GetByIdAsync(existingKey.ResultNotificationId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(original.ProviderMessageSid) && !original.IsTerminalStatus)
        {
            try
            {
                var live = await _twilio.FetchMessageAsync(original.ProviderMessageSid, cancellationToken);
                original.ApplyProviderState(live.Status, live.ErrorCode, original.ContentRedacted ? null : live.Body);
                await _notifications.UpdateAsync(original, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId} before resend: {Message}", original.Id, ex.Message);
            }
        }

        if (original.HasReachedShopper)
        {
            throw new InvalidOperationException("This message already reached the shopper.");
        }

        if (original.IsInFlight)
        {
            throw new InvalidOperationException("This message is still in progress with the provider.");
        }

        var destination = await ResolveActiveDestinationAsync(original, cancellationToken);
        if (destination == null)
        {
            throw new InvalidOperationException("The destination number is no longer on file for this shopper.");
        }

        var body = original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body)
            ? OrderSmsTemplates.For(original.Kind, original.OrderId)
            : original.Body;

        var send = await TrySendAsync(destination.PhoneNumber, body, sendAt: null, cancellationToken);
        var resent = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            destination.Id,
            destination.PhoneNumber,
            body,
            send?.Sid,
            send?.Status ?? "failed",
            send?.ErrorCode,
            scheduledSendAt: null,
            resentFromNotificationId: original.Id);

        await _notifications.AddAsync(resent, cancellationToken);
        await _resendRecords.AddAsync(
            new NotificationResendRecord(original.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);

        _logger.LogInformation(
            "Resent notification {OriginalNotificationId} as {NotificationId} for order {OrderId}.",
            original.Id, resent.Id, original.OrderId);

        return resent;
    }

    public async Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _twilio.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(updated.Status, updated.ErrorCode, body: null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Provider redaction failed for notification {NotificationId}: {Message}",
                    notification.Id, ex.Message);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content for notification {NotificationId}.", notification.Id);
        return notification;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromNumber = _twilio.FromNumber;
        var providerMessages = await _twilio.ListMessagesFromAsync(fromNumber, from, to, cancellationToken);
        var applicationMessages = await _notifications.ListAsync(
            new NotificationsInDateRangeSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var applicationBySid = applicationMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.ProviderMessageSid))
            .GroupBy(m => m.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciledMessage>();
        var providerOnly = new List<ProviderOnlyMessage>();
        var applicationOnly = new List<ApplicationOnlyMessage>();

        foreach (var provider in providerBySid.Values)
        {
            if (applicationBySid.TryGetValue(provider.Sid!, out var local))
            {
                matched.Add(new ReconciledMessage(local.Id, provider.Sid!, local.ProviderStatus, provider.Status));
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage(provider.Sid!, provider.Status, provider.DateCreated));
            }
        }

        foreach (var local in applicationMessages)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderMessageSid)
                || !providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                applicationOnly.Add(new ApplicationOnlyMessage(local.Id, local.ProviderMessageSid, local.ProviderStatus));
            }
        }

        return new NotificationReconciliationReport(from, to, fromNumber, matched, providerOnly, applicationOnly);
    }

    private async Task SendKindAsync(
        Order order,
        NotificationKind kind,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await GetPreferredDestinationAsync(order.BuyerId, cancellationToken);
            if (destination == null)
            {
                _logger.LogInformation(
                    "No contact number on file for buyer {BuyerId}; skipping {Kind} for order {OrderId}.",
                    order.BuyerId, kind, order.Id);
                return;
            }

            var body = OrderSmsTemplates.For(kind, order.Id);
            var send = await TrySendAsync(destination.PhoneNumber, body, sendAt, cancellationToken);
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                destination.Id,
                destination.PhoneNumber,
                body,
                send?.Sid,
                send?.Status ?? "failed",
                send?.ErrorCode,
                sendAt,
                resentFromNotificationId: null);

            await _notifications.AddAsync(notification, cancellationToken);
            _logger.LogInformation(
                "Recorded {Kind} notification {NotificationId} for order {OrderId} with status {Status}.",
                kind, notification.Id, order.Id, notification.ProviderStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to send {Kind} notification for order {OrderId}: {Message}",
                kind, order.Id, ex.Message);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(
            new ScheduledFollowUpsByOrderIdSpecification(orderId), cancellationToken);

        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var live = followUp.IsTerminalStatus
                    ? null
                    : await _twilio.FetchMessageAsync(followUp.ProviderMessageSid, cancellationToken);

                if (live != null)
                {
                    followUp.ApplyProviderState(live.Status, live.ErrorCode, followUp.ContentRedacted ? null : live.Body);
                }

                if (followUp.IsScheduled
                    || string.Equals(followUp.ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(followUp.ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase))
                {
                    var cancelled = await _twilio.CancelMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                    followUp.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, followUp.ContentRedacted ? null : cancelled.Body);
                    _logger.LogInformation(
                        "Cancelled scheduled follow-up {NotificationId} for order {OrderId}.",
                        followUp.Id, orderId);
                }

                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not cancel follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid) || notification.IsTerminalStatus)
            {
                continue;
            }

            try
            {
                var live = await _twilio.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(
                    live.Status,
                    live.ErrorCode,
                    notification.ContentRedacted ? null : live.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not refresh provider status for notification {NotificationId}: {Message}",
                    notification.Id, ex.Message);
            }
        }
    }

    private async Task<ProviderMessageResult?> TrySendAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _twilio.SendMessageAsync(new SendProviderMessageRequest(to, body, sendAt), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider send failed: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<ContactNumber?> GetPreferredDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<ContactNumber?> ResolveActiveDestinationAsync(
        OrderNotification original,
        CancellationToken cancellationToken)
    {
        if (original.ContactNumberId.HasValue)
        {
            var byId = await _contactNumbers.FirstOrDefaultAsync(
                new ContactNumberByIdAndBuyerSpecification(original.ContactNumberId.Value, original.BuyerId),
                cancellationToken);
            if (byId != null)
            {
                return byId;
            }
        }

        var remaining = await _contactNumbers.ListAsync(
            new ContactNumbersByBuyerIdSpecification(original.BuyerId), cancellationToken);
        return remaining.FirstOrDefault(c =>
            string.Equals(c.PhoneNumber, original.DestinationNumber, StringComparison.Ordinal));
    }
}
