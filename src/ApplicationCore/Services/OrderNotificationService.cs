using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read"
    };

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IContactNumberService _contactNumbers;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IClock _clock;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IContactNumberService contactNumbers,
        ITwilioMessagingClient messaging,
        IClock clock,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _clock = clock;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => SendForOrderAsync(order, NotificationKind.OrderPlaced, BuildBody(NotificationKind.OrderPlaced, order.Id), sendAt: null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendForOrderAsync(order, NotificationKind.OrderDispatched, BuildBody(NotificationKind.OrderDispatched, order.Id), sendAt: null, cancellationToken);
        var followUpAt = _clock.UtcNow.Add(DeliveryFollowUpDelay);
        await SendForOrderAsync(order, NotificationKind.DeliveryFollowUp, BuildBody(NotificationKind.DeliveryFollowUp, order.Id), followUpAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        await SendForOrderAsync(order, NotificationKind.OrderCancelled, BuildBody(NotificationKind.OrderCancelled, order.Id), sendAt: null, cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOrderStateException("An idempotency key is required.");
        }

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(notificationId, idempotencyKey.Trim()),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ResourceNotFoundException("Notification not found.");

        await RefreshOneAsync(original, cancellationToken);

        if (!original.DidNotReachShopper())
        {
            throw new InvalidOrderStateException("Only messages that did not reach the shopper can be resent.");
        }

        var contact = await _contactNumbers.GetLatestForBuyerAsync(original.BuyerId, cancellationToken);
        if (contact is null)
        {
            throw new InvalidOrderStateException("The shopper has no contact number on file.");
        }

        var body = original.ContentRedacted || string.IsNullOrEmpty(original.Body)
            ? BuildBody(original.Kind, original.OrderId)
            : original.Body;

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            original.Kind,
            body,
            idempotencyKey: idempotencyKey.Trim(),
            sourceNotificationId: original.Id);

        await TrySendAsync(resend, contact.PhoneNumber, sendAt: null, cancellationToken);
        return await _notifications.AddAsync(resend, cancellationToken);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new ResourceNotFoundException("Notification not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messaging.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryOutcome(updated.Status, updated.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact provider content for notification {NotificationId}: {ExceptionType}", notification.Id, ex.GetType().Name);
                throw;
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshManyAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyList<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        if (refreshFromProvider)
        {
            await RefreshManyAsync(notifications, cancellationToken);
        }

        return notifications;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidOrderStateException("The reconciliation range is invalid: 'to' must be on or after 'from'.");
        }

        var providerMessages = await _messaging.ListByFromNumberAsync(from, to, cancellationToken);
        var applicationNotifications = await _notifications.ListAsync(
            new OrderNotificationsByCreatedRangeSpecification(from, to),
            cancellationToken);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var notification in applicationNotifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) ||
                providerBySid.ContainsKey(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var fetched = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (fetched is not null)
                {
                    providerBySid[fetched.Sid] = fetched;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to fetch provider message for notification {NotificationId}: {ExceptionType}", notification.Id, ex.GetType().Name);
            }
        }

        var bySid = applicationNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<ProviderOnlyMessage>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in providerBySid.Values)
        {
            seenSids.Add(message.Sid);
            if (bySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch
                {
                    NotificationId = notification.Id,
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    ApplicationStatus = notification.ProviderStatus,
                    Kind = notification.Kind
                });
            }
            else
            {
                providerOnly.Add(new ProviderOnlyMessage
                {
                    ProviderMessageSid = message.Sid,
                    Status = message.Status,
                    Direction = message.Direction,
                    DateCreated = message.DateCreated,
                    DateSent = message.DateSent
                });
            }
        }

        var applicationOnly = applicationNotifications
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !seenSids.Contains(n.ProviderMessageSid))
            .Select(n => new ApplicationOnlyNotification
            {
                NotificationId = n.Id,
                ProviderMessageSid = n.ProviderMessageSid,
                Status = n.ProviderStatus,
                Kind = n.Kind,
                CreatedAt = n.CreatedAt
            })
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _messaging.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            ApplicationOnly = applicationOnly
        };
    }

    private async Task SendForOrderAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await _contactNumbers.GetLatestForBuyerAsync(order.BuyerId, cancellationToken);
            if (contact is null)
            {
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, body, sendAt);
            await TrySendAsync(notification, contact.PhoneNumber, sendAt, cancellationToken);
            await _notifications.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}: {ExceptionType}", kind, order.Id, ex.GetType().Name);
        }
    }

    private async Task TrySendAsync(
        OrderNotification notification,
        string to,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messaging.SendAsync(new SendProviderMessageRequest
            {
                To = to,
                Body = notification.Body ?? BuildBody(notification.Kind, notification.OrderId),
                SendAt = sendAt
            }, cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider rejected {Kind} notification for order {OrderId}: {ExceptionType}", notification.Kind, notification.OrderId, ex.GetType().Name);
            notification.RecordFailure("failed", errorCode: null);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpByOrderIdSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                var updated = await _messaging.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryOutcome(updated.Status, updated.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {ExceptionType}", followUp.Id, orderId, ex.GetType().Name);
                continue;
            }

            await _notifications.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task RefreshManyAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await RefreshOneAsync(notification, cancellationToken);
        }
    }

    private async Task RefreshOneAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        if (TerminalStatuses.Contains(notification.ProviderStatus))
        {
            return;
        }

        try
        {
            var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (current is null)
            {
                return;
            }

            notification.UpdateDeliveryOutcome(current.Status, current.ErrorCode);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}: {ExceptionType}", notification.Id, ex.GetType().Name);
        }
    }

    public static string BuildBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"Your eShop order #{orderId} has been placed. Thank you.",
        NotificationKind.OrderDispatched => $"Your eShop order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"How did the delivery of eShop order #{orderId} go? Reply with any feedback.",
        NotificationKind.OrderCancelled => $"Your eShop order #{orderId} has been cancelled.",
        _ => $"An update is available for eShop order #{orderId}."
    };
}
