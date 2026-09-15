using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Default <see cref="IOrderNotificationService"/>. Sends order messages through the provider on a
/// best-effort basis, persists a record of every message (sent or not) together with the state the
/// provider owns, and drives the follow-up / resend / disposal / status-refresh behaviours.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // Provider delivery states we treat as final, so refreshing them again would tell us nothing.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "failed", "undelivered", "canceled", Notification.SendErrorStatus
    };

    /// <summary>How far ahead the "how did the delivery go?" follow-up is scheduled.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Notification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Notification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IReadRepository<Order> orderRepository,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _orderRepository = orderRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        var body = OrderMessageComposer.Compose(order, NotificationType.OrderPlaced);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, NotificationType.OrderPlaced, number, body, sendAt: null, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        var dispatchedBody = OrderMessageComposer.Compose(order, NotificationType.OrderDispatched);
        var followUpBody = OrderMessageComposer.Compose(order, NotificationType.DeliveryFollowUp);
        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, NotificationType.OrderDispatched, number, dispatchedBody, sendAt: null, cancellationToken);
            // The follow-up is queued with the provider for a few days later — not held here for a timer of our own.
            await SendAndRecordAsync(order, NotificationType.DeliveryFollowUp, number, followUpBody, sendAt: followUpAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        var body = OrderMessageComposer.Compose(order, NotificationType.OrderCancelled);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, NotificationType.OrderCancelled, number, body, sendAt: null, cancellationToken);
        }

        // A delivery follow-up that has not gone out yet must never reach a customer whose order was
        // cancelled — call it off at the provider.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
    }

    public async Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        // A repeat under the same key must not send a second message: return what the first attempt produced.
        var alreadyDone = await _notificationRepository
            .FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone != null)
        {
            _logger.LogInformation($"Resend under idempotency key already handled; returning notification {alreadyDone.Id}.");
            return alreadyDone;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        var body = original.Body;
        if (string.IsNullOrEmpty(body))
        {
            // Content was disposed of (or never captured) — rebuild it from the order so the resend is faithful.
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(original.OrderId), cancellationToken);
            body = order != null
                ? OrderMessageComposer.Compose(order, original.Type)
                : $"eShopOnWeb: An update about your order #{original.OrderId}.";
        }

        var resend = new Notification(original.BuyerId, original.OrderId, NotificationType.Resend, original.ToNumber, body);
        resend.AttachIdempotencyKey(idempotencyKey);
        await TrySendAsync(resend, new SmsSendRequest(original.ToNumber, body), cancellationToken);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        _logger.LogInformation($"Resent notification {notificationId} as {resend.Id} (order {resend.OrderId}, status {resend.ProviderStatus}).");
        return resend;
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        // Dispose of the text at the provider first so it is genuinely gone there, then locally. If the
        // provider redaction fails we surface the error rather than falsely reporting success.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid) && !notification.ContentRedacted)
        {
            await _smsProvider.RedactMessageBodyAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Disposed of content for notification {notificationId} (order {notification.OrderId}).");
        return true;
    }

    public async Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
    }

    public async Task RefreshBuyerNotificationStatusesAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
    }

    private async Task RefreshAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            if (notification.ProviderStatus != null && TerminalStatuses.Contains(notification.ProviderStatus))
            {
                continue;
            }

            try
            {
                var message = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(message.Status, message.ErrorCode, ScrubProviderError(message.ErrorCode, message.ErrorMessage));
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // A failed refresh must not break the read; keep the last-known state.
                _logger.LogWarning($"Could not refresh delivery state for notification {notification.Id}: {ex.GetType().Name}.");
            }
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.Type == NotificationType.DeliveryFollowUp && n.IsPendingScheduledDelivery()))
        {
            try
            {
                await _smsProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryState("canceled", null, null);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation($"Called off scheduled follow-up {followUp.Id} for cancelled order {orderId}.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not call off follow-up {followUp.Id} for order {orderId}: {ex.GetType().Name}.");
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        // A shopper with no number on file is simply not messaged.
        return numbers.Select(n => n.PhoneNumber).ToList();
    }

    private async Task SendAndRecordAsync(Order order, NotificationType type, string toNumber, string body,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var notification = new Notification(order.BuyerId, order.Id, type, toNumber, body);
        if (sendAt.HasValue)
        {
            notification.MarkScheduled(sendAt.Value);
        }

        await TrySendAsync(notification, new SmsSendRequest(toNumber, body, sendAt), cancellationToken);
        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    /// <summary>
    /// Attempts the send and records the outcome onto the notification. Never throws: a message that
    /// cannot be sent is recorded as a failure so the order operation still succeeds.
    /// </summary>
    private async Task TrySendAsync(Notification notification, SmsSendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _smsProvider.SendAsync(request, cancellationToken);
            notification.RecordSent(message.Sid, message.Status, message.ErrorCode, ScrubProviderError(message.ErrorCode, message.ErrorMessage));
            _logger.LogInformation(
                $"Sent {notification.Type} for order {notification.OrderId} (sid {message.Sid}, status {message.Status}).");
        }
        catch (Exception ex)
        {
            // Record the failure without ever putting the destination number into a log.
            notification.RecordSendFailure(ex.GetType().Name);
            _logger.LogWarning(
                $"Could not send {notification.Type} for order {notification.OrderId}: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// Reduces a provider error to a code-tagged, number-free string. Provider error messages can echo
    /// the destination number, which must never be stored where it could surface in a log or response.
    /// </summary>
    private static string? ScrubProviderError(int? errorCode, string? errorMessage)
    {
        if (errorCode.HasValue)
        {
            return $"Provider error {errorCode.Value}";
        }

        return string.IsNullOrEmpty(errorMessage) ? null : "Provider reported an error";
    }
}
