using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates shopper notifications as orders move. Every provider interaction is
/// best-effort for the order lifecycle methods: a message that cannot be sent is
/// recorded as failed and never fails the underlying operation. Phone numbers are
/// never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> ResendableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed", "undelivered"
    };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsProvider smsProvider,
        TwilioSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsProvider = smsProvider;
        _settings = settings;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = FormattableString.Invariant($"eShop: thank you! Your order #{order.Id} totalling ${order.Total():0.00} was placed. We'll text you when it ships.");
        await SendToShopperAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: good news — order #{order.Id} is on its way!";
        await SendToShopperAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        // The follow-up is queued with the provider itself (scheduled send), not held in-app.
        var followUpBody = $"eShop: order #{order.Id} should have arrived by now — how did the delivery go?";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await SendToShopperAsync(order, NotificationType.DeliveryFollowUp, followUpBody, cancellationToken, scheduleFor: sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A follow-up that has not yet gone out must never reach the shopper.
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                await _smsProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateStatus("canceled", null, null);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled provider message {MessageSid} for order {OrderId}" + ": " + ex.Message, followUp.ProviderMessageSid!, order.Id);
            }
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, cancellationToken);
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications.Where(n => !n.IsTerminal && n.ProviderMessageSid != null))
        {
            try
            {
                var current = await _smsProvider.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                if (current != null)
                {
                    notification.UpdateStatus(current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status for provider message {MessageSid}: " + ex.Message, notification.ProviderMessageSid!);
            }
        }
    }

    public async Task<(OrderNotification Notification, bool IdempotentReplay)> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return (existing, true);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new KeyNotFoundException($"Notification {notificationId} was not found.");
        }

        if (original.ContentRedacted || original.Body == null)
        {
            throw new InvalidOperationException("The content of this message has been disposed of and it can no longer be resent.");
        }

        if (!ResendableStatuses.Contains(original.Status))
        {
            throw new InvalidOperationException($"Only a message that did not reach the shopper can be resent (current status: {original.Status}).");
        }

        // A removed contact number must never be sent to again.
        var contactNumber = await _contactNumbers.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != original.BuyerId)
        {
            throw new InvalidOperationException("The destination number is no longer registered for this shopper; the message cannot be resent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            original.ToNumber, NotificationType.Resend, original.Body);
        resend.MarkAsResend(idempotencyKey, original.Id);

        try
        {
            var message = await _smsProvider.SendMessageAsync(original.ToNumber, original.Body, cancellationToken);
            resend.MarkAccepted(message.Sid, message.Status);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed(ex.Message);
            _logger.LogWarning("Resend of notification {NotificationId} was rejected by the provider" + ": " + ex.Message, notificationId);
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return (resend, false);
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new KeyNotFoundException($"Notification {notificationId} was not found.");
        }

        if (notification.ContentRedacted)
        {
            return;
        }

        // Redact at the provider too: the text must no longer be retrievable there either.
        if (notification.ProviderMessageSid != null)
        {
            await _smsProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsProvider.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notifications.ListAsync(new NotificationsInRangeSpecification(from, to), cancellationToken);
        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _settings.FromNumber
        };

        foreach (var message in providerMessages.OrderBy(m => m.DateCreated))
        {
            var matched = localBySid.TryGetValue(message.Sid, out var notification);
            if (matched && notification != null)
            {
                // Reconciliation doubles as a status sweep for anything stale locally.
                notification.UpdateStatus(message.Status, message.ErrorCode, message.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }

            report.ProviderMessages.Add(new ReconciliationEntry
            {
                MessageSid = message.Sid,
                To = message.To,
                Status = message.Status,
                DateSent = message.DateSent,
                MatchStatus = matched ? "Matched" : "ProviderOnly",
                NotificationId = notification?.Id
            });
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));
        report.LocalOnly = localNotifications
            .Where(n => n.ProviderMessageSid != null && !providerSids.Contains(n.ProviderMessageSid!))
            .ToList();

        report.ProviderMessageCount = providerMessages.Count;
        report.MatchedCount = report.ProviderMessages.Count(m => m.MatchStatus == "Matched");
        report.ProviderOnlyCount = report.ProviderMessages.Count(m => m.MatchStatus == "ProviderOnly");
        report.LocalOnlyCount = report.LocalOnly.Count;
        return report;
    }

    private async Task SendToShopperAsync(Order order, NotificationType type, string body,
        CancellationToken cancellationToken, DateTimeOffset? scheduleFor = null)
    {
        List<ContactNumber> numbers;
        try
        {
            numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load contact numbers for order {OrderId}; skipping {Type} notification" + ": " + ex.Message, order.Id, type);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.Id, number.PhoneNumber, type, body);
            try
            {
                var message = scheduleFor.HasValue
                    ? await _smsProvider.ScheduleMessageAsync(number.PhoneNumber, body, scheduleFor.Value, cancellationToken)
                    : await _smsProvider.SendMessageAsync(number.PhoneNumber, body, cancellationToken);
                notification.MarkAccepted(message.Sid, message.Status, scheduleFor);
            }
            catch (Exception ex)
            {
                // Never fail the underlying operation because a message could not go out.
                notification.MarkSendFailed(ex.Message);
                _logger.LogWarning("Provider rejected {Type} notification for order {OrderId}" + ": " + ex.Message, type, order.Id);
            }

            try
            {
                await _notifications.AddAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not record {Type} notification for order {OrderId}" + ": " + ex.Message, type, order.Id);
            }
        }
    }
}
