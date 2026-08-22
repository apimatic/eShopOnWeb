using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
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

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IContactNumberService _contactNumberService;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IContactNumberService contactNumberService,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberService = contactNumberService;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order)
    {
        var body = $"eShopOnWeb: Your order #{order.Id} has been placed. Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}.";
        return TrySendAsync(order, OrderNotificationKind.OrderPlaced, body);
    }

    public async Task NotifyOrderDispatchedAsync(Order order)
    {
        var dispatchedBody = $"eShopOnWeb: Your order #{order.Id} is on its way.";
        await TrySendAsync(order, OrderNotificationKind.OrderDispatched, dispatchedBody);

        var followUpBody = $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await TrySendAsync(order, OrderNotificationKind.DeliveryFollowUp, followUpBody, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order)
    {
        await CancelOutstandingFollowUpsAsync(order.Id);

        var body = $"eShopOnWeb: Your order #{order.Id} has been cancelled.";
        await TrySendAsync(order, OrderNotificationKind.OrderCancelled, body);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string buyerId)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdSpecification(orderId));
        var owned = notifications.Where(n => n.BuyerId == buyerId).ToList();
        await RefreshStatusesAsync(owned);
        return owned;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerIdSpecification(buyerId));
        await RefreshStatusesAsync(notifications);
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationResendByIdempotencyKeySpecification(notificationId, idempotencyKey));
        if (existing is not null)
        {
            await RefreshStatusesAsync(new[] { existing });
            return existing;
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId);
        if (source is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        if (source.ContentRedacted || string.IsNullOrWhiteSpace(source.Body))
        {
            throw new InvalidOperationException("The original message content is no longer available to resend.");
        }

        var destination = await _contactNumberService.GetActiveDestinationAsync(source.BuyerId);
        if (destination is null)
        {
            throw new InvalidOperationException("The shopper no longer has a reachable number on file.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            source.Body,
            destination.PhoneNumber,
            sourceNotificationId: source.Id,
            resendIdempotencyKey: idempotencyKey,
            originalKind: source.Kind == OrderNotificationKind.Resend ? source.OriginalKind ?? source.Kind : source.Kind);

        resend = await _notificationRepository.AddAsync(resend);
        await DispatchToProviderAsync(resend, destination.PhoneNumber, source.Body, sendAt: null);
        return resend;
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            try
            {
                var updated = await _messagingClient.UpdateAsync(
                    notification.ProviderMessageSid,
                    new SmsUpdate { RedactBody = true });
                if (!string.IsNullOrWhiteSpace(updated.Status))
                {
                    notification.ApplyProviderOutcome(updated.Status, updated.ErrorCode);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to redact provider content for notification {NotificationId} (order {OrderId}).",
                    notification.Id,
                    notification.OrderId);
                throw;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification);
        return notification;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' instant must be on or after 'from'.");
        }

        var fromNumber = _messagingClient.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _messagingClient.ListFromNumberAsync(fromNumber, from, to);
        var localInRange = await _notificationRepository.ListAsync(new NotificationsInCreatedRangeSpecification(from, to));

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localBySid = localInRange
            .Where(n => !string.IsNullOrWhiteSpace(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var items = new List<NotificationReconciliationItem>();

        foreach (var provider in providerBySid.Values)
        {
            localBySid.TryGetValue(provider.Sid!, out var local);
            items.Add(new NotificationReconciliationItem
            {
                Match = local is null ? "providerOnly" : "matched",
                ProviderSid = provider.Sid,
                ProviderStatus = provider.Status,
                ProviderDateSent = provider.DateSent ?? provider.DateCreated,
                NotificationId = local?.Id,
                Kind = local?.Kind.ToString()
            });
        }

        foreach (var local in localInRange)
        {
            if (!string.IsNullOrWhiteSpace(local.ProviderMessageSid) &&
                providerBySid.ContainsKey(local.ProviderMessageSid))
            {
                continue;
            }

            items.Add(new NotificationReconciliationItem
            {
                Match = "localOnly",
                ProviderSid = local.ProviderMessageSid,
                ProviderStatus = local.ProviderStatus,
                ProviderDateSent = local.ScheduledFor ?? local.CreatedAt,
                NotificationId = local.Id,
                Kind = local.Kind.ToString()
            });
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Items = items,
            MatchedCount = items.Count(i => i.Match == "matched"),
            ProviderOnlyCount = items.Count(i => i.Match == "providerOnly"),
            LocalOnlyCount = items.Count(i => i.Match == "localOnly")
        };
    }

    private async Task TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null)
    {
        try
        {
            var destination = await _contactNumberService.GetActiveDestinationAsync(order.BuyerId);
            if (destination is null)
            {
                _logger.LogInformation(
                    "Skipping {Kind} notification for order {OrderId} because the shopper has no number on file.",
                    kind,
                    order.Id);
                return;
            }

            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                body,
                destination.PhoneNumber,
                sendAt);

            notification = await _notificationRepository.AddAsync(notification);
            await DispatchToProviderAsync(notification, destination.PhoneNumber, body, sendAt);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Failed to send {Kind} notification for order {OrderId}. The order operation was not affected.",
                kind,
                order.Id);
        }
    }

    private async Task DispatchToProviderAsync(
        OrderNotification notification,
        string destination,
        string body,
        DateTimeOffset? sendAt)
    {
        try
        {
            var sent = await _messagingClient.SendAsync(new OutgoingSms
            {
                To = destination,
                Body = body,
                SendAt = sendAt
            });

            if (!string.IsNullOrWhiteSpace(sent.Sid) && !string.IsNullOrWhiteSpace(sent.Status))
            {
                notification.RecordProviderAcceptance(sent.Sid, sent.Status, sent.ErrorCode);
            }
            else
            {
                notification.RecordProviderFailure(sent.ErrorCode, sent.Status ?? "failed");
            }
        }
        catch (Exception)
        {
            notification.RecordProviderFailure(null);
            _logger.LogWarning(
                "Provider rejected {Kind} notification {NotificationId} for order {OrderId}.",
                notification.Kind,
                notification.Id,
                notification.OrderId);
        }

        await _notificationRepository.UpdateAsync(notification);
    }

    private async Task CancelOutstandingFollowUpsAsync(int orderId)
    {
        var followUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderIdSpecification(orderId));
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrWhiteSpace(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var updated = await _messagingClient.UpdateAsync(
                    followUp.ProviderMessageSid,
                    new SmsUpdate { Cancel = true });
                followUp.ApplyProviderOutcome(updated.Status ?? "canceled", updated.ErrorCode);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}.",
                    followUp.Id,
                    orderId);
            }

            await _notificationRepository.UpdateAsync(followUp);
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            {
                continue;
            }

            if (TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid);
                if (current is null || string.IsNullOrWhiteSpace(current.Status))
                {
                    continue;
                }

                notification.ApplyProviderOutcome(current.Status, current.ErrorCode);
                await _notificationRepository.UpdateAsync(notification);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }
}
