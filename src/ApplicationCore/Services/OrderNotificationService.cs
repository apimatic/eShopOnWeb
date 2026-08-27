using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The delivery follow-up is queued with the provider itself, this many days after dispatch.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsMessagingClient _smsMessagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsMessagingClient smsMessagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsMessagingClient = smsMessagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: thank you! Your order #{order.Id} has been placed.";
        return SendToBuyerAsync(order, NotificationType.OrderPlaced, body, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: good news - your order #{order.Id} has been dispatched and is on its way.";
        await SendToBuyerAsync(order, NotificationType.OrderDispatched, body, null, cancellationToken);

        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await SendToBuyerAsync(order, NotificationType.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToBuyerAsync(order, NotificationType.OrderCancelled, body, null, cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return new ResendResult { Notification = existing, Replayed = true };
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new EntityNotFoundException($"Notification {notificationId} was not found.");
        }

        if (original.ContentRedacted || original.Body == null)
        {
            throw new NotificationConflictException($"Notification {notificationId} can no longer be resent because its content has been disposed of.");
        }

        ContactNumber? contactNumber = null;
        if (original.ContactNumberId.HasValue)
        {
            contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId.Value, cancellationToken);
        }

        if (contactNumber == null || contactNumber.BuyerId != original.BuyerId)
        {
            throw new NotificationConflictException($"Notification {notificationId} cannot be resent because the destination contact number is no longer registered.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, contactNumber.Id,
            contactNumber.PhoneNumber, original.Type, original.Body, null, idempotencyKey);

        try
        {
            var result = await _smsMessagingClient.SendMessageAsync(contactNumber.PhoneNumber, original.Body, null, cancellationToken);
            resend.RecordProviderAcceptance(result.MessageSid, result.Status);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed(ex.Message);
            _logger.LogWarning("Resend of notification {NotificationId} for order {OrderId} was rejected by the provider ({ExceptionType}).",
                notificationId, original.OrderId, ex.GetType().Name);
        }

        var saved = await _notificationRepository.AddAsync(resend, cancellationToken);
        return new ResendResult { Notification = saved, Replayed = false };
    }

    public async Task DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new EntityNotFoundException($"Notification {notificationId} was not found.");
        }

        if (notification.ContentRedacted)
        {
            return;
        }

        if (notification.ProviderMessageSid != null)
        {
            await _smsMessagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);

        foreach (var notification in notifications.Where(n => n.ProviderMessageSid != null && !n.IsTerminal))
        {
            try
            {
                var details = await _smsMessagingClient.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                if (details != null)
                {
                    notification.UpdateStatus(details.Status, details.ErrorCode, details.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status of notification {NotificationId} for order {OrderId} ({ExceptionType}).",
                    notification.Id, orderId, ex.GetType().Name);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = (await _smsMessagingClient.ListMessagesFromSendingNumberAsync(cancellationToken))
            .Where(m => m.DateCreated.HasValue && m.DateCreated.Value >= from && m.DateCreated.Value <= to)
            .ToList();

        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport { From = from, To = to };

        foreach (var providerMessage in providerMessages.OrderBy(m => m.DateCreated))
        {
            if (localBySid.TryGetValue(providerMessage.MessageSid, out var local))
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = providerMessage.MessageSid,
                    NotificationId = local.Id,
                    To = providerMessage.To,
                    ProviderStatus = providerMessage.Status,
                    LocalStatus = local.Status,
                    DateCreated = providerMessage.DateCreated,
                    DateSent = providerMessage.DateSent,
                    Match = "Matched"
                });
                report.MatchedCount++;
            }
            else
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = providerMessage.MessageSid,
                    To = providerMessage.To,
                    ProviderStatus = providerMessage.Status,
                    DateCreated = providerMessage.DateCreated,
                    DateSent = providerMessage.DateSent,
                    Match = "ProviderOnly"
                });
                report.ProviderOnlyCount++;
            }
        }

        foreach (var local in localNotifications.OrderBy(n => n.CreatedAt))
        {
            var knownToProvider = local.ProviderMessageSid != null &&
                                  providerMessages.Any(m => m.MessageSid == local.ProviderMessageSid);
            if (!knownToProvider)
            {
                report.Entries.Add(new ReconciliationEntry
                {
                    MessageSid = local.ProviderMessageSid,
                    NotificationId = local.Id,
                    LocalStatus = local.Status,
                    DateCreated = local.CreatedAt,
                    Match = "ShopOnly"
                });
                report.ShopOnlyCount++;
            }
        }

        return report;
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);

        var pendingFollowUps = notifications.Where(n =>
            n.Type == NotificationType.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            !n.IsTerminal);

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var details = await _smsMessagingClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (details != null)
                {
                    followUp.UpdateStatus(details.Status, details.ErrorCode, details.ErrorMessage);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up notification {NotificationId} for order {OrderId} ({ExceptionType}).",
                    followUp.Id, orderId, ex.GetType().Name);
            }
        }
    }

    private async Task SendToBuyerAsync(Order order, NotificationType type, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);

        if (contactNumbers.Count == 0)
        {
            _logger.LogInformation("Order {OrderId}: no contact number on file for the buyer; skipping {NotificationType} notification.", order.Id, type);
            return;
        }

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id,
                contactNumber.PhoneNumber, type, body, sendAt);

            try
            {
                var result = await _smsMessagingClient.SendMessageAsync(contactNumber.PhoneNumber, body, sendAt, cancellationToken);
                notification.RecordProviderAcceptance(result.MessageSid, result.Status);
            }
            catch (Exception ex)
            {
                // The order operation must never fail because a message could not be sent.
                // The provider's error message can embed the destination number, so it is
                // recorded on the notification but deliberately kept out of the logs.
                notification.MarkSendFailed(ex.Message);
                _logger.LogWarning("Order {OrderId}: {NotificationType} notification could not be sent ({ExceptionType}).",
                    order.Id, type, ex.GetType().Name);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }
}
