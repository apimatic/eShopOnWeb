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
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IMessagingClient _messagingClient;
    private readonly NotificationSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Order> orderRepository,
        IMessagingClient messagingClient,
        NotificationSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _orderRepository = orderRepository;
        _messagingClient = messagingClient;
        _settings = settings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        return NotifyAsync(order, NotificationType.OrderPlaced,
            $"eShopOnWeb: Thank you! Your order #{order.Id} has been placed.",
            scheduledFor: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        await NotifyAsync(order, NotificationType.OrderDispatched,
            $"eShopOnWeb: Good news! Your order #{order.Id} is on its way.",
            scheduledFor: null, cancellationToken);

        // The follow-up is queued with the provider itself (scheduled send), not held in-app.
        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        await NotifyAsync(order, NotificationType.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of your order #{order.Id} go? We'd love to hear from you.",
            scheduledFor: sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        await NotifyAsync(order, NotificationType.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled. Please contact support if this is unexpected.",
            scheduledFor: null, cancellationToken);

        // A follow-up that has not yet gone out must never reach the shopper.
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
        var pendingFollowUps = notifications
            .Where(n => n.Type == NotificationType.DeliveryFollowUp
                        && n.MessageSid is not null
                        && !NotificationStatus.IsTerminal(n.Status)
                        && n.DateSent is null)
            .ToList();

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var providerMessage = await _messagingClient.CancelScheduledMessageAsync(followUp.MessageSid!, cancellationToken);
                followUp.ApplyProviderState(providerMessage.Status, providerMessage.ErrorCode,
                    providerMessage.ErrorMessage, providerMessage.DateSent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {MessageSid} for order {OrderId} at the provider: {Error}",
                    followUp.MessageSid, order.Id, ex.Message);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string callerId, bool callerIsOperator, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || (!callerIsOperator && order.BuyerId != callerId))
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        // No provider callback URL exists, so current outcomes are obtained by asking the provider.
        foreach (var notification in notifications.Where(n => n.MessageSid is not null && !NotificationStatus.IsTerminal(n.Status)))
        {
            try
            {
                var providerMessage = await _messagingClient.FetchMessageAsync(notification.MessageSid!, cancellationToken);
                notification.ApplyProviderState(providerMessage.Status, providerMessage.ErrorCode,
                    providerMessage.ErrorMessage, providerMessage.DateSent);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider state for message {MessageSid}; returning last known state. {Error}",
                    notification.MessageSid, ex.Message);
            }
        }

        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult(ResendOutcome.AlreadyProcessed, existing);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult(ResendOutcome.NotificationNotFound, null);
        }
        if (original.ContentRedacted || original.Body is null)
        {
            return new ResendResult(ResendOutcome.ContentRedacted, null,
                "The message content has been disposed of and can no longer be sent.");
        }

        // A removed contact number must never be sent to again.
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(original.BuyerId), cancellationToken);
        if (!contactNumbers.Any(c => c.PhoneNumber == original.ToNumber))
        {
            return new ResendResult(ResendOutcome.DestinationNoLongerRegistered, null,
                "The destination number is no longer registered for this shopper.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationType.Resend,
            original.ToNumber, original.Body, scheduledFor: null,
            idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);

        await SendAndRecordAsync(resend, cancellationToken);
        return new ResendResult(ResendOutcome.Sent, resend);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the text at the provider too, so it is no longer retrievable there.
        if (notification.MessageSid is not null)
        {
            var providerMessage = await _messagingClient.RedactMessageBodyAsync(notification.MessageSid, cancellationToken);
            notification.ApplyProviderState(providerMessage.Status, providerMessage.ErrorCode,
                providerMessage.ErrorMessage, providerMessage.DateSent);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (from >= to)
        {
            throw new ArgumentException("The 'from' date-time must be earlier than the 'to' date-time.", nameof(from));
        }

        // Ask the provider for this application's own sending number's messages only.
        var providerMessages = await _messagingClient.ListMessagesAsync(
            _messagingClient.FromNumber, from, to, cancellationToken);

        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        var matchedLocalIds = new HashSet<int>();

        foreach (var message in providerMessages.OrderBy(m => m.DateSent))
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matchedLocalIds.Add(local.Id);
                entries.Add(new ReconciliationEntry(message.Sid, local.Id, message.Status,
                    local.Status, message.DateSent, ReconciliationMatchState.Matched));
            }
            else
            {
                entries.Add(new ReconciliationEntry(message.Sid, null, message.Status,
                    null, message.DateSent, ReconciliationMatchState.ProviderOnly));
            }
        }

        foreach (var local in localNotifications.Where(n => !matchedLocalIds.Contains(n.Id)))
        {
            entries.Add(new ReconciliationEntry(local.MessageSid, local.Id, null,
                local.Status, local.DateSent, ReconciliationMatchState.LocalOnly));
        }

        return new ReconciliationReport(from, to, _messagingClient.FromNumber, entries)
        {
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count,
            MatchedCount = entries.Count(e => e.MatchState == ReconciliationMatchState.Matched),
            ProviderOnlyCount = entries.Count(e => e.MatchState == ReconciliationMatchState.ProviderOnly),
            LocalOnlyCount = entries.Count(e => e.MatchState == ReconciliationMatchState.LocalOnly)
        };
    }

    /// <summary>
    /// Notifies the shopper on their most recently registered number, if any.
    /// Best-effort: provider failures are recorded, never thrown.
    /// </summary>
    private async Task NotifyAsync(Order order, NotificationType type, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var target = contactNumbers.LastOrDefault();
        if (target is null)
        {
            _logger.LogInformation("Order {OrderId}: buyer has no contact number on file; no {Type} notification sent.",
                order.Id, type);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, type, target.PhoneNumber, body, scheduledFor);
        await SendAndRecordAsync(notification, cancellationToken);
    }

    private async Task SendAndRecordAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var providerMessage = await _messagingClient.SendMessageAsync(
                notification.ToNumber, notification.Body!, notification.ScheduledFor, cancellationToken);
            notification.MarkAccepted(providerMessage.Sid, providerMessage.Status);
            notification.ApplyProviderState(providerMessage.Status, providerMessage.ErrorCode,
                providerMessage.ErrorMessage, providerMessage.DateSent);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.MarkFailed(null, ex.Message);
            _logger.LogWarning("Notification {Type} for order {OrderId} could not be sent; recorded as failed. {Error}",
                notification.Type, notification.OrderId, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}
