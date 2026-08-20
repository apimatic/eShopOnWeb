using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IContactNumberService _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IContactNumberService contactNumbers,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
        => TrySendAsync(orderId, buyerId, OrderNotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{orderId} has been placed.", sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await TrySendAsync(orderId, buyerId, OrderNotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{orderId} is on its way.", sendAt: null, cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await TrySendAsync(orderId, buyerId, OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{orderId} go? We would love your feedback.",
            followUpAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(orderId, cancellationToken);

        await TrySendAsync(orderId, buyerId, OrderNotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{orderId} has been cancelled.", sendAt: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
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
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new ResendByIdempotencySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                     ?? throw new KeyNotFoundException("Notification not found.");

        if (!string.IsNullOrEmpty(source.ProviderMessageSid))
        {
            try
            {
                var current = await _messaging.FetchAsync(source.ProviderMessageSid, cancellationToken);
                source.ApplyProviderState(current.Status ?? source.ProviderStatus, current.ErrorCode, current.Body, current.Sid);
                await _notifications.UpdateAsync(source, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh notification {NotificationId} before resend: {Error}", notificationId, ex.Message);
            }
        }

        if (!source.DidNotReachShopper() && !string.IsNullOrEmpty(source.ProviderMessageSid))
        {
            throw new InvalidOrderStateException("Only messages that did not reach the shopper can be re-sent.");
        }

        var stillRegistered = await _contactNumbers.IsStillRegisteredAsync(source.BuyerId, source.Destination, cancellationToken);
        if (!stillRegistered)
        {
            throw new InvalidOrderStateException("The destination number is no longer on file; nothing will be sent to it.");
        }

        var body = source.Body;
        if (string.IsNullOrEmpty(body) || source.ContentRedacted)
        {
            body = ReconstructBody(source);
        }

        var sent = await SendIgnoringFailuresAsync(source.Destination, body, sendAt: null, cancellationToken);
        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            source.Destination,
            body,
            sent?.Sid,
            sent?.Status ?? "failed",
            sent?.ErrorCode,
            scheduledSendAt: null,
            resendOfNotificationId: source.Id,
            idempotencyKey: idempotencyKey.Trim());

        return await _notifications.AddAsync(resend, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new KeyNotFoundException("Notification not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var updated = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            notification.ApplyProviderState(updated.Status ?? notification.ProviderStatus, updated.ErrorCode, updated.Body, updated.Sid);
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

        var providerMessages = await _messaging.ListSentFromAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationRow>();
        var providerOnly = new List<ReconciliationRow>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerMessages)
        {
            if (string.IsNullOrEmpty(message.Sid))
            {
                continue;
            }

            seenSids.Add(message.Sid);
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(ToRow(notification, message));
            }
            else
            {
                providerOnly.Add(new ReconciliationRow
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    DateCreated = message.DateCreated
                });
            }
        }

        var localOnly = local
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !seenSids.Contains(n.ProviderMessageSid))
            .Select(n => ToRow(n, provider: null))
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _messaging.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            LocalOnly = localOnly
        };
    }

    private async Task TrySendAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await _contactNumbers.GetPreferredForBuyerAsync(buyerId, cancellationToken);
            if (contact is null)
            {
                _logger.LogInformation("No contact number on file for buyer; skipping {Kind} for order {OrderId}.", kind, orderId);
                return;
            }

            var sent = await SendIgnoringFailuresAsync(contact.PhoneNumber, body, sendAt, cancellationToken);
            var notification = new OrderNotification(
                orderId,
                buyerId,
                kind,
                contact.PhoneNumber,
                body,
                sent?.Sid,
                sent?.Status ?? "failed",
                sent?.ErrorCode,
                sendAt);

            await _notifications.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} failed: {Error}", kind, orderId, ex.Message);
        }
    }

    private async Task<ProviderMessage?> SendIgnoringFailuresAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _messaging.SendAsync(new SendProviderMessageRequest
            {
                To = to,
                Body = body,
                SendAt = sendAt
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider send failed: {Error}", ex.Message);
            return null;
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpNotificationsSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                var status = (current.Status ?? followUp.ProviderStatus).ToLowerInvariant();
                if (status is "scheduled" or "accepted" or "queued")
                {
                    current = await _messaging.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                }

                followUp.ApplyProviderState(current.Status ?? followUp.ProviderStatus, current.ErrorCode, current.Body, current.Sid);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel follow-up notification {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(current.Status ?? notification.ProviderStatus, current.ErrorCode, current.Body, current.Sid);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }

    private static string ReconstructBody(OrderNotification source)
    {
        return source.Kind switch
        {
            OrderNotificationKind.OrderPlaced => $"Your eShopOnWeb order #{source.OrderId} has been placed.",
            OrderNotificationKind.OrderDispatched => $"Your eShopOnWeb order #{source.OrderId} is on its way.",
            OrderNotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{source.OrderId} go? We would love your feedback.",
            OrderNotificationKind.OrderCancelled => $"Your eShopOnWeb order #{source.OrderId} has been cancelled.",
            _ => $"An update about your eShopOnWeb order #{source.OrderId}."
        };
    }

    private static ReconciliationRow ToRow(OrderNotification notification, ProviderMessage? provider)
    {
        return new ReconciliationRow
        {
            NotificationId = notification.Id,
            ProviderMessageSid = notification.ProviderMessageSid ?? provider?.Sid,
            LocalStatus = notification.ProviderStatus,
            ProviderStatus = provider?.Status,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            DateSent = provider?.DateSent,
            DateCreated = notification.CreatedAt
        };
    }
}
