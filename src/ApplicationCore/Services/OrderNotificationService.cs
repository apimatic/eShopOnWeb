using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IContactNumberService _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IContactNumberService contactNumbers,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: order #{orderId} is placed. Reply STOP to unsubscribe.";
        return SendOrSkipAsync(orderId, buyerId, OrderNotificationKind.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: order #{orderId} is on its way.";
        await SendOrSkipAsync(orderId, buyerId, OrderNotificationKind.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShopOnWeb: how did delivery of order #{orderId} go?";
        await ScheduleOrSkipAsync(
            orderId,
            buyerId,
            OrderNotificationKind.DeliveryFollowUp,
            followUpBody,
            DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await CancelScheduledFollowUpsAsync(orderId, cancellationToken);

        var body = $"eShopOnWeb: order #{orderId} has been cancelled.";
        await SendOrSkipAsync(orderId, buyerId, OrderNotificationKind.OrderCancelled, body, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await SyncProviderStateAsync(notification, cancellationToken);
        }

        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        await SyncProviderStateAsync(original, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new InvalidOperationException("Only messages that did not reach the shopper can be resent.");
        }

        if (!await _contactNumbers.IsStillRegisteredAsync(original.BuyerId, original.DestinationNumber, cancellationToken))
        {
            throw new InvalidOperationException("The destination number is no longer on file and cannot be messaged.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            await SyncProviderStateAsync(existing, cancellationToken);
            return existing;
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            OrderNotificationKind.Resend,
            original.DestinationNumber,
            original.ContentRedacted ? $"eShopOnWeb: a previous update about order #{original.OrderId} is being resent." : original.Body,
            idempotencyKey,
            notificationId);

        await _notifications.AddAsync(resend, cancellationToken);
        await TrySendAsync(resend, scheduled: false, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var updated = await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(updated.Status ?? notification.ProviderStatus, updated.ErrorCode);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messagingClient.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var eShopNotifications = await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var providerSids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<NotificationReconciliationEntry>();

        foreach (var provider in providerMessages.Where(m => !string.IsNullOrEmpty(m.Sid)))
        {
            providerSids.Add(provider.Sid!);
            if (eShopBySid.TryGetValue(provider.Sid!, out var local))
            {
                entries.Add(new NotificationReconciliationEntry(
                    "matched",
                    local.Id,
                    provider.Sid,
                    local.ProviderStatus,
                    provider.Status));
            }
            else
            {
                entries.Add(new NotificationReconciliationEntry(
                    "providerOnly",
                    null,
                    provider.Sid,
                    null,
                    provider.Status));
            }
        }

        foreach (var local in eShopNotifications)
        {
            if (string.IsNullOrEmpty(local.ProviderMessageSid) || !providerSids.Contains(local.ProviderMessageSid))
            {
                entries.Add(new NotificationReconciliationEntry(
                    "eShopOnly",
                    local.Id,
                    local.ProviderMessageSid,
                    local.ProviderStatus,
                    null));
            }
        }

        return new NotificationReconciliationReport(from, to, _messagingClient.FromNumber, entries);
    }

    public async Task SyncProviderStateAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(current.Status ?? notification.ProviderStatus, current.ErrorCode);
            if (string.IsNullOrEmpty(current.Body) && !string.IsNullOrEmpty(notification.Body))
            {
                notification.MarkContentRedacted();
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh provider state for notification {NotificationId}: {Message}", notification.Id, ex.Message);
        }
    }

    private async Task SendOrSkipAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        CancellationToken cancellationToken)
    {
        var destination = await _contactNumbers.GetPreferredForBuyerAsync(buyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("No contact number on file for this shopper; skipping {Kind} for order {OrderId}.", kind, orderId);
            return;
        }

        var notification = new OrderNotification(orderId, buyerId, kind, destination.CanonicalNumber, body);
        await _notifications.AddAsync(notification, cancellationToken);
        await TrySendAsync(notification, scheduled: false, cancellationToken);
    }

    private async Task ScheduleOrSkipAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken)
    {
        var destination = await _contactNumbers.GetPreferredForBuyerAsync(buyerId, cancellationToken);
        if (destination is null)
        {
            _logger.LogInformation("No contact number on file for this shopper; skipping scheduled {Kind} for order {OrderId}.", kind, orderId);
            return;
        }

        var notification = new OrderNotification(orderId, buyerId, kind, destination.CanonicalNumber, body, scheduledSendAt: sendAt);
        await _notifications.AddAsync(notification, cancellationToken);
        await TrySendAsync(notification, scheduled: true, cancellationToken);
    }

    private async Task TrySendAsync(OrderNotification notification, bool scheduled, CancellationToken cancellationToken)
    {
        try
        {
            var result = scheduled && notification.ScheduledSendAt.HasValue
                ? await _messagingClient.ScheduleAsync(notification.DestinationNumber, notification.Body, notification.ScheduledSendAt.Value, cancellationToken)
                : await _messagingClient.SendAsync(notification.DestinationNumber, notification.Body, cancellationToken);

            if (string.IsNullOrEmpty(result.Sid))
            {
                notification.RecordLocalFailure("Provider accepted the request without a message identifier.");
            }
            else
            {
                notification.RecordProviderAcceptance(result.Sid, result.Status ?? (scheduled ? "scheduled" : "queued"));
                if (result.ErrorCode.HasValue)
                {
                    notification.ApplyProviderState(result.Status ?? notification.ProviderStatus, result.ErrorCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider send failed for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            notification.RecordLocalFailure("The messaging provider rejected or failed the send.");
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                var updated = await _messagingClient.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.ApplyProviderState(updated.Status ?? "canceled", updated.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId}: {Message}", followUp.Id, ex.Message);
                try
                {
                    await SyncProviderStateAsync(followUp, cancellationToken);
                }
                catch
                {
                    // Best-effort refresh; the cancel SMS for the order still goes out.
                }
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }
}
