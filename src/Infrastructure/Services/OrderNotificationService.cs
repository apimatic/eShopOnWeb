using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Orchestrates order notifications over the Twilio client. Sending is always best-effort: a message
/// that cannot be handed to the provider is recorded as a failure but never bubbles up to fail the order
/// operation. No phone number or message body is ever written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did delivery go?" follow-up is queued with the provider.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read", "partially_delivered"
    };

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly string _fromNumber;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient twilio,
        IOptions<TwilioSettings> settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _twilio = twilio;
        _logger = logger;
        _fromNumber = settings.Value.FromNumber;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetNumbersAsync(order.BuyerId, cancellationToken);
        var body = $"eShop: your order #{order.Id} has been placed. Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}.";
        foreach (var number in numbers)
        {
            await SendNowAsync(order, NotificationType.OrderPlaced, number, body, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetNumbersAsync(order.BuyerId, cancellationToken);
        var dispatchedBody = $"eShop: good news! Your order #{order.Id} is on its way.";
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? Reply to let us know.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            await SendNowAsync(order, NotificationType.OrderDispatched, number, dispatchedBody, cancellationToken);
            await ScheduleFollowUpAsync(order, number, followUpBody, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // First call off any follow-up already queued for this order so a cancelled delivery is never
        // asked about, then tell the shopper the order was cancelled.
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsForOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            await CancelScheduledAsync(followUp, cancellationToken);
        }

        var numbers = await GetNumbersAsync(order.BuyerId, cancellationToken);
        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.";
        foreach (var number in numbers)
        {
            await SendNowAsync(order, NotificationType.OrderCancelled, number, body, cancellationToken);
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // A repeat under a key already seen must not send again: return what that key first produced.
        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        var body = original.Body ?? DefaultBodyFor(original.Type, original.OrderId);
        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Type, original.ToNumber, body);
        resend.SetIdempotencyKey(idempotencyKey);

        try
        {
            var msg = await _twilio.SendAsync(original.ToNumber, body, cancellationToken);
            resend.RecordAccepted(msg.Sid, msg.Status);
        }
        catch (Exception ex)
        {
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning("Resend of notification {NotificationId} for order {OrderId} could not be handed to the provider.",
                notificationId, original.OrderId);
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (notification.ContentDisposed)
        {
            return;
        }

        // Redact at the provider first so the text is genuinely gone there, not merely hidden here.
        if (!string.IsNullOrEmpty(notification.ProviderSid))
        {
            await _twilio.RedactBodyAsync(notification.ProviderSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content of notification {NotificationId} for order {OrderId}.",
            notification.Id, notification.OrderId);
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }
            if (notification.ProviderStatus is not null && TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var msg = await _twilio.FetchAsync(notification.ProviderSid, cancellationToken);
                notification.ApplyProviderState(msg.Status, msg.ErrorCode, msg.ErrorMessage, msg.DateSent);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                // Best effort: keep the last known outcome if the provider cannot be reached right now.
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task CancelScheduledForContactNumberAsync(string buyerId, string toNumber, CancellationToken cancellationToken = default)
    {
        var scheduled = await _notifications.ListAsync(
            new ScheduledNotificationsForContactNumberSpecification(buyerId, toNumber), cancellationToken);
        foreach (var followUp in scheduled)
        {
            await CancelScheduledAsync(followUp, cancellationToken);
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _twilio.ListByFromAsync(_fromNumber, fromUtc, toUtc, cancellationToken);

        // Bound precisely to the requested window (the provider filters by whole GMT day).
        var providerInRange = providerMessages
            .Where(m => m.DateSent.HasValue && m.DateSent.Value >= fromUtc && m.DateSent.Value <= toUtc)
            .ToList();

        var eShopNotifications = await _notifications.ListAsync(new NotificationsWithProviderSidSpecification(), cancellationToken);
        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerSids = new HashSet<string>(providerInRange.Select(m => m.Sid), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var msg in providerInRange)
        {
            if (eShopBySid.TryGetValue(msg.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(msg.Sid, msg.Status, msg.DateSent,
                    notification.Id, notification.OrderId, notification.Type.ToString()));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(msg.Sid, msg.Status, msg.DateSent, null, null, null));
            }
        }

        var eShopOnly = new List<ReconciliationEntry>();
        var eShopInRangeCount = 0;
        foreach (var notification in eShopNotifications)
        {
            var effectiveTime = notification.ProviderSentAt ?? notification.CreatedAt;
            if (effectiveTime < fromUtc || effectiveTime > toUtc)
            {
                continue;
            }
            eShopInRangeCount++;

            if (!providerSids.Contains(notification.ProviderSid!))
            {
                eShopOnly.Add(new ReconciliationEntry(notification.ProviderSid, notification.ProviderStatus,
                    notification.ProviderSentAt, notification.Id, notification.OrderId, notification.Type.ToString()));
            }
        }

        return new ReconciliationReport(fromUtc, toUtc, _fromNumber,
            providerInRange.Count, eShopInRangeCount, matched, providerOnly, eShopOnly);
    }

    // ----- helpers --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> GetNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task SendNowAsync(Order order, NotificationType type, string toNumber, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toNumber, body);
        try
        {
            var msg = await _twilio.SendAsync(toNumber, body, cancellationToken);
            notification.RecordAccepted(msg.Sid, msg.Status);
        }
        catch (Exception)
        {
            notification.RecordSendFailure("Provider did not accept the message.");
            _logger.LogWarning("{Type} message for order {OrderId} could not be handed to the provider.", type, order.Id);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, toNumber, body);
        try
        {
            var msg = await _twilio.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.RecordAccepted(msg.Sid, msg.Status, isScheduled: true, scheduledSendAt: sendAt);
        }
        catch (Exception)
        {
            notification.RecordSendFailure("Provider did not accept the scheduled follow-up.");
            _logger.LogWarning("Delivery follow-up for order {OrderId} could not be scheduled with the provider.", order.Id);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(followUp.ProviderSid))
        {
            return;
        }
        try
        {
            await _twilio.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
            followUp.MarkCanceled();
            await _notifications.UpdateAsync(followUp, cancellationToken);
            _logger.LogInformation("Called off scheduled follow-up {NotificationId} for order {OrderId}.",
                followUp.Id, followUp.OrderId);
        }
        catch (Exception)
        {
            _logger.LogWarning("Could not call off scheduled follow-up {NotificationId} for order {OrderId}.",
                followUp.Id, followUp.OrderId);
        }
    }

    private static string DefaultBodyFor(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShop: your order #{orderId} has been placed.",
        NotificationType.OrderDispatched => $"eShop: your order #{orderId} is on its way.",
        NotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go?",
        NotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
