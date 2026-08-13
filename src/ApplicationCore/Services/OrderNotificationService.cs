using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far in the future the delivery-feedback follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FeedbackDelay = TimeSpan.FromDays(3);

    /// <summary>Provider status of a message it has accepted but not yet sent.</summary>
    private const string ScheduledStatus = "scheduled";

    private readonly IRepository<Notification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Notification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        ISmsGateway sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order)
    {
        Guard.Against.Null(order, nameof(order));
        var number = await GetActiveNumberAsync(order.BuyerId);
        if (number is null)
        {
            _logger.LogInformation("Order {OrderId}: no contact number on file; placed message not sent.", order.Id);
            return;
        }

        await SendAndRecordAsync(order, NotificationType.OrderPlaced, number,
            $"eShopOnWeb: your order #{order.Id} has been placed. Thank you for shopping with us!");
    }

    public async Task NotifyOrderDispatchedAsync(Order order)
    {
        Guard.Against.Null(order, nameof(order));
        var number = await GetActiveNumberAsync(order.BuyerId);
        if (number is null)
        {
            _logger.LogInformation("Order {OrderId}: no contact number on file; dispatch message not sent.", order.Id);
            return;
        }

        await SendAndRecordAsync(order, NotificationType.OrderDispatched, number,
            $"eShopOnWeb: good news - your order #{order.Id} is on its way!");

        // Queue the delivery-feedback follow-up with the provider itself for a few days later.
        // The application holds no timer of its own.
        await ScheduleFeedbackAsync(order, number);
    }

    public async Task NotifyOrderCancelledAsync(Order order)
    {
        Guard.Against.Null(order, nameof(order));

        var number = await GetActiveNumberAsync(order.BuyerId);
        if (number is not null)
        {
            await SendAndRecordAsync(order, NotificationType.OrderCancelled, number,
                $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.");
        }
        else
        {
            _logger.LogInformation("Order {OrderId}: no contact number on file; cancel message not sent.", order.Id);
        }

        // A not-yet-sent feedback follow-up must never reach the shopper for a cancelled order.
        // Call it off with the provider regardless of whether a number is currently on file.
        await CancelScheduledFeedbackAsync(order.Id);
    }

    public async Task<Notification> ResendAsync(int notificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: repeating under the same key returns the message the first attempt produced,
        // without sending a second.
        var priorForKey = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey));
        if (priorForKey is not null)
        {
            _logger.LogInformation("Resend for notification {NotificationId} is a repeat under a known key; returning existing notification {ExistingId}.",
                notificationId, priorForKey.Id);
            return priorForKey;
        }

        var original = await _notifications.GetByIdAsync(notificationId)
            ?? throw new KeyNotFoundException($"No notification found with id {notificationId}.");

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new InvalidOperationException(
                $"Notification {notificationId} has no content to resend (its content was disposed of).");
        }

        var resend = new Notification(original.OrderId, original.OwnerId, original.Type, original.ToNumber,
            original.Body, idempotencyKey);
        await TrySendAsync(resend, () => _sms.SendAsync(resend.ToNumber, original.Body),
            $"resend of notification {notificationId}");
        await _notifications.AddAsync(resend);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId)
    {
        var notification = await _notifications.GetByIdAsync(notificationId)
            ?? throw new KeyNotFoundException($"No notification found with id {notificationId}.");

        if (notification.ContentDisposed)
        {
            return; // already disposed; nothing more to do
        }

        // Redact at the provider so the text is no longer retrievable there either. If the provider
        // never accepted the message there is nothing to redact. A provider failure here is surfaced,
        // because the caller asked for the content to be genuinely gone.
        if (notification.ProviderSid is not null)
        {
            await _sms.RedactBodyAsync(notification.ProviderSid);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId));

        // Refresh each message to its current provider outcome so callers see where it got to.
        foreach (var notification in notifications.Where(n => n.ProviderSid is not null))
        {
            await RefreshAsync(notification);
        }

        return notifications;
    }

    public Task<Notification?> FindNotificationAsync(int notificationId) =>
        _notifications.GetByIdAsync(notificationId);

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        // The provider's own record of messages from the application's configured sending number.
        var providerMessages = await _sms.ListSentFromConfiguredNumberAsync(from, to);
        var providerFrom = providerMessages.Select(m => m.From).FirstOrDefault(f => !string.IsNullOrEmpty(f));

        // Everything eShop has a record of (this run), keyed by provider identifier.
        var allLocal = await _notifications.ListAsync();
        var localBySid = allLocal
            .Where(n => n.ProviderSid is not null)
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = providerFrom ?? string.Empty
        };

        var providerSids = new HashSet<string>();
        foreach (var message in providerMessages)
        {
            if (message.Sid is null)
            {
                continue;
            }
            providerSids.Add(message.Sid);

            var target = localBySid.TryGetValue(message.Sid, out var local) ? report.Matched : report.ProviderOnly;
            target.Add(new ReconciliationEntry
            {
                ProviderSid = message.Sid,
                NotificationId = local?.Id,
                Status = message.Status,
                SentAt = message.SentAt
            });
        }

        // eShop-only: local records in range the provider's answer did not include — including sends
        // the provider never accepted (no identifier at all).
        foreach (var local in allLocal.Where(n => n.CreatedAt >= from && n.CreatedAt <= to))
        {
            if (local.ProviderSid is not null && providerSids.Contains(local.ProviderSid))
            {
                continue;
            }

            report.EShopOnly.Add(new ReconciliationEntry
            {
                ProviderSid = local.ProviderSid,
                NotificationId = local.Id,
                Status = local.ProviderStatus,
                SentAt = local.ProviderSentAt ?? local.CreatedAt
            });
        }

        return report;
    }

    private async Task<string?> GetActiveNumberAsync(string ownerId)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        // ContactNumbersByOwnerSpecification orders most-recent first.
        return numbers.FirstOrDefault()?.E164Number;
    }

    private async Task<Notification> SendAndRecordAsync(Order order, NotificationType type, string number, string body)
    {
        var notification = new Notification(order.Id, order.BuyerId, type, number, body);
        await TrySendAsync(notification, () => _sms.SendAsync(number, body), $"{type} for order {order.Id}");
        await _notifications.AddAsync(notification);
        return notification;
    }

    private async Task ScheduleFeedbackAsync(Order order, string number)
    {
        var sendAt = DateTimeOffset.UtcNow.Add(FeedbackDelay);
        var body = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var notification = new Notification(order.Id, order.BuyerId, NotificationType.DeliveryFeedback, number, body);
        try
        {
            var state = await _sms.ScheduleAsync(number, body, sendAt);
            notification.RecordProviderAccepted(state.Sid, state.Status, state.ErrorCode, state.ErrorMessage,
                scheduledSendAt: sendAt, sentAt: state.SentAt);
            _logger.LogInformation("Order {OrderId}: delivery-feedback follow-up scheduled with provider ({Status}).",
                order.Id, state.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: delivery-feedback follow-up could not be scheduled ({Reason}); order operation unaffected.",
                order.Id, ex.Message);
            notification.RecordNotSent(ex.Message);
        }
        await _notifications.AddAsync(notification);
    }

    private async Task CancelScheduledFeedbackAsync(int orderId)
    {
        var feedbacks = await _notifications.ListAsync(new ScheduledFeedbackByOrderSpecification(orderId));
        foreach (var feedback in feedbacks)
        {
            if (feedback.ProviderSid is null || feedback.ProviderStatus != ScheduledStatus)
            {
                continue; // nothing queued at the provider to call off
            }

            try
            {
                var state = await _sms.CancelScheduledAsync(feedback.ProviderSid);
                feedback.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(feedback);
                _logger.LogInformation("Order {OrderId}: called off scheduled delivery-feedback follow-up (now {Status}).",
                    orderId, state.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: could not call off scheduled feedback follow-up ({Reason}).",
                    orderId, ex.Message);
            }
        }
    }

    private async Task RefreshAsync(Notification notification)
    {
        try
        {
            var state = await _sms.FetchAsync(notification.ProviderSid!);
            notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage, state.SentAt);
            await _notifications.UpdateAsync(notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification {NotificationId}: could not refresh provider status ({Reason}).",
                notification.Id, ex.Message);
        }
    }

    private async Task TrySendAsync(Notification notification, Func<Task<SmsMessageState>> send, string what)
    {
        try
        {
            var state = await send();
            notification.RecordProviderAccepted(state.Sid, state.Status, state.ErrorCode, state.ErrorMessage,
                sentAt: state.SentAt);
            _logger.LogInformation("Sent {What}: provider status {Status}.", what, state.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Could not send {What} ({Reason}); underlying operation unaffected.", what, ex.Message);
            notification.RecordNotSent(ex.Message);
        }
    }
}
