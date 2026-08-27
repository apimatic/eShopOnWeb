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

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the delivery follow-up is queued for.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly string[] ResendableStatuses = { "failed", "undelivered" };

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessageProvider _messageProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessageProvider messageProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messageProvider = messageProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        return NotifyAsync(order, NotificationKind.OrderPlaced,
            $"eShop: Thank you! Your order #{order.Id} has been placed (total ${order.Total():0.00}). We'll text you when it ships.",
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        await NotifyAsync(order, NotificationKind.OrderDispatched,
            $"eShop: Good news! Your order #{order.Id} has been dispatched and is on its way.",
            cancellationToken);

        // Queue the delivery follow-up with the provider itself for a few days later.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAsync(order, NotificationKind.DeliveryFollowUp,
            $"eShop: Your order #{order.Id} should have arrived by now. How did the delivery go? We'd love your feedback.",
            cancellationToken, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        await NotifyAsync(order, NotificationKind.OrderCancelled,
            $"eShop: Your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
            cancellationToken);

        // A follow-up that has not gone out yet must never reach a cancelled order's shopper.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                var cancelled = await _messageProvider.CancelScheduledAsync(followUp.MessageSid!, cancellationToken);
                followUp.UpdateDeliveryStatus(cancelled ? "canceled" : followUp.Status, null);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up {MessageSid} for order {OrderId}: {Outcome}",
                    followUp.MessageSid ?? string.Empty, order.Id, cancelled ? "canceled" : "provider did not confirm");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {MessageSid} for order {OrderId}: {Error}",
                    followUp.MessageSid, order.Id, ex.Message);
            }
        }
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(notification, nameof(notification));
        if (string.IsNullOrEmpty(notification.MessageSid))
        {
            return;
        }

        try
        {
            var providerMessage = await _messageProvider.GetAsync(notification.MessageSid, cancellationToken);
            if (providerMessage != null && providerMessage.Status != notification.Status)
            {
                notification.UpdateDeliveryStatus(providerMessage.Status, providerMessage.ErrorCode);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh status for message {MessageSid}: {Error}", notification.MessageSid, ex.Message);
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return ResendResult.Duplicate(existing);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return ResendResult.Failed($"Notification {notificationId} was not found.");
        }
        if (original.ContentRedacted || original.Body == null)
        {
            return ResendResult.Failed("The message content has been disposed of and can no longer be sent.");
        }
        if (!ResendableStatuses.Contains(original.Status))
        {
            return ResendResult.Failed($"Only messages that did not reach the shopper can be re-sent (current status: {original.Status}).");
        }

        // A deleted contact number must never be sent to again.
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(original.BuyerId), cancellationToken);
        if (!contactNumbers.Any(c => c.PhoneNumber == original.ToNumber))
        {
            return ResendResult.Failed("The destination number is no longer registered for this shopper.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber, original.Body,
            original.Kind, null, original.Id, idempotencyKey);
        await SubmitAsync(resend, scheduledFor: null, cancellationToken);
        return ResendResult.Sent(resend);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notification.MessageSid))
        {
            try
            {
                await _messageProvider.RedactBodyAsync(notification.MessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider failed to redact body of message {MessageSid}: {Error}", notification.MessageSid, ex.Message);
                throw;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content of notification {NotificationId} (message {MessageSid})",
            notification.Id, notification.MessageSid ?? string.Empty);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messageProvider.ListSentAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.MessageSid))
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _messageProvider.FromNumber,
            ProviderMessageCount = providerMessages.Count,
            LocalMessageCount = localNotifications.Count(n => !string.IsNullOrEmpty(n.MessageSid))
        };

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                report.Matched.Add(new ReconciliationEntry
                {
                    MessageSid = message.Sid,
                    NotificationId = local.Id,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status,
                    DateSent = message.DateSent
                });
            }
            else
            {
                report.ProviderOnly.Add(new ReconciliationEntry
                {
                    MessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent
                });
            }
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));
        foreach (var local in localNotifications.Where(n => !string.IsNullOrEmpty(n.MessageSid)))
        {
            if (!providerSids.Contains(local.MessageSid!))
            {
                report.LocalOnly.Add(new ReconciliationEntry
                {
                    MessageSid = local.MessageSid!,
                    NotificationId = local.Id,
                    LocalStatus = local.Status
                });
            }
        }

        return report;
    }

    private async Task NotifyAsync(Order order, NotificationKind kind, string body, CancellationToken cancellationToken,
        DateTimeOffset? scheduledFor = null)
    {
        List<ContactNumber> contactNumbers;
        try
        {
            contactNumbers = await _contactNumberRepository.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load contact numbers for order {OrderId}; skipping {Kind} notification: {Error}", order.Id, kind, ex.Message);
            return;
        }

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.PhoneNumber, body, kind, scheduledFor);
            await SubmitAsync(notification, scheduledFor, cancellationToken);
        }
    }

    private async Task SubmitAsync(OrderNotification notification, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            var result = scheduledFor.HasValue
                ? await _messageProvider.ScheduleAsync(notification.ToNumber, notification.Body!, scheduledFor.Value, cancellationToken)
                : await _messageProvider.SendAsync(notification.ToNumber, notification.Body!, cancellationToken);

            if (result.Succeeded && result.MessageSid != null)
            {
                notification.MarkSubmitted(result.MessageSid, result.Status);
            }
            else
            {
                notification.MarkSubmissionFailed(result.Status, result.ErrorCode);
                _logger.LogWarning("Message for order {OrderId} ({Kind}) was not accepted by the provider: {Status} {ErrorCode}",
                    notification.OrderId, notification.Kind, result.Status, result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            notification.MarkSubmissionFailed("error", null);
            _logger.LogWarning("Sending message for order {OrderId} ({Kind}) failed; the order operation is unaffected: {Error}",
                notification.OrderId, notification.Kind, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}
