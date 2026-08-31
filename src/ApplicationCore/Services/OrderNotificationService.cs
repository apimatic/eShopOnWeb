using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order SMS notifications. A message that cannot be sent never fails
/// the underlying order operation; the failure is recorded on the notification instead.
/// Shopper phone numbers are never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} was placed. Total: ${order.Total():0.00}. Thank you for shopping with us!";
        return SendToShopperAsync(order, NotificationType.OrderPlaced, body, null, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news! Your order #{order.Id} is on its way.";
        await SendToShopperAsync(order, NotificationType.OrderDispatched, body, null, null, cancellationToken);

        // Queue the delivery follow-up with the provider itself for a few days later;
        // nothing is held in this application to be sent by a timer of its own.
        var followUpBody = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        await SendToShopperAsync(order, NotificationType.DeliveryFollowUp, followUpBody,
            DateTimeOffset.UtcNow.Add(FollowUpDelay), null, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, null, null, cancellationToken);

        // A follow-up that has not yet gone out must never reach the shopper.
        var scheduledFollowUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpec(order.Id), cancellationToken);
        foreach (var followUp in scheduledFollowUps)
        {
            try
            {
                var message = await _messagingClient.CancelMessageAsync(followUp.MessageSid!, cancellationToken);
                followUp.UpdateFromProvider(message.Status, message.ErrorCode, message.ErrorMessage, message.DateSent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up (notification {NotificationId}, message {MessageSid}) with the provider.",
                    followUp.Id, followUp.MessageSid ?? string.Empty);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpec(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return ResendNotificationResult.IdempotentReplay(existing);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return ResendNotificationResult.NotFound();
        }

        if (original.ContentRedacted || original.Body == null)
        {
            return ResendNotificationResult.ContentRedacted();
        }

        // The destination must still be on file; a removed number is never sent to again.
        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber == null)
        {
            return ResendNotificationResult.DestinationRemoved();
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, contactNumber.Id,
            contactNumber.PhoneNumber, original.Type, original.Body, null, idempotencyKey);
        await SendAsync(resend, contactNumber.PhoneNumber, original.Body, null, cancellationToken);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        return ResendNotificationResult.Resent(resend);
    }

    public async Task<RedactContentResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return RedactContentResult.NotFound();
        }

        if (notification.ContentRedacted)
        {
            return RedactContentResult.Redacted();
        }

        // Redact at the provider first: the text must no longer be retrievable there,
        // not merely hidden by this application.
        if (notification.MessageSid != null)
        {
            try
            {
                await _messagingClient.RedactMessageBodyAsync(notification.MessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to redact message {MessageSid} (notification {NotificationId}) at the provider.",
                    notification.MessageSid, notification.Id);
                return RedactContentResult.ProviderError("The message content could not be disposed of at the provider.");
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return RedactContentResult.Redacted();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messagingClient.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsInRangeSpec(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count
        };

        var matchedSids = new HashSet<string>();
        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matchedSids.Add(message.Sid);
                report.MatchedCount++;
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = message.Sid,
                    NotificationId = local.Id,
                    To = message.To,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    MatchStatus = ReconciliationEntry.Matched
                });
            }
            else
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = message.Sid,
                    To = message.To,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    MatchStatus = ReconciliationEntry.MissingLocally
                });
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.MessageSid == null || !matchedSids.Contains(local.MessageSid))
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = local.MessageSid,
                    NotificationId = local.Id,
                    To = local.ToNumber,
                    ProviderStatus = local.ProviderStatus,
                    DateSent = local.SentAt,
                    MatchStatus = ReconciliationEntry.MissingAtProvider
                });
            }
        }

        return report;
    }

    public async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.MessageSid == null || notification.IsTerminal)
        {
            return;
        }

        try
        {
            var message = await _messagingClient.FetchMessageAsync(notification.MessageSid, cancellationToken);
            notification.UpdateFromProvider(message.Status, message.ErrorCode, message.ErrorMessage, message.DateSent);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh delivery state for message {MessageSid} (notification {NotificationId}).",
                notification.MessageSid, notification.Id);
        }
    }

    private async Task SendToShopperAsync(Order order, NotificationType type, string body,
        DateTimeOffset? sendAt, string? idempotencyKey, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContactNumber> contactNumbers;
        try
        {
            contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load contact numbers for order {OrderId}; skipping {NotificationType} notification.", order.Id, type);
            return;
        }

        // A shopper with no number on file is simply not messaged.
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id,
                contactNumber.PhoneNumber, type, body, sendAt, idempotencyKey);
            await SendAsync(notification, contactNumber.PhoneNumber, body, sendAt, cancellationToken);
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task SendAsync(OrderNotification notification, string toNumber, string body,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messagingClient.CreateMessageAsync(toNumber, body, sendAt, cancellationToken);
            notification.MarkAccepted(message.Sid, message.Status);
            _logger.LogInformation("Notification {NotificationType} for order {OrderId} accepted by provider as {MessageSid} ({Status}).",
                notification.Type, notification.OrderId, message.Sid, message.Status);
        }
        catch (TwilioApiException ex)
        {
            notification.MarkFailed(ex.ErrorCode, ex.Message);
            _logger.LogWarning("Provider rejected notification {NotificationType} for order {OrderId}: {ErrorCode} {ErrorMessage}",
                notification.Type, notification.OrderId, ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            notification.MarkFailed(null, "The message could not be sent.");
            _logger.LogError(ex, "Unexpected failure sending notification {NotificationType} for order {OrderId}.",
                notification.Type, notification.OrderId);
        }
    }
}
