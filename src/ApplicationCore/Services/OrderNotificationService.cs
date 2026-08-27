using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IMessageProvider _messageProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly NotificationSettings _settings;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IMessageProvider messageProvider,
        IAppLogger<OrderNotificationService> logger,
        NotificationSettings settings)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messageProvider = messageProvider;
        _logger = logger;
        _settings = settings;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAllAsync(order, NotificationKind.OrderPlaced,
            $"eShop: Thank you! Your order #{order.Id} has been placed.", null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAllAsync(order, NotificationKind.OrderDispatched,
            $"eShop: Good news! Your order #{order.Id} is on its way.", null, cancellationToken);

        // Queue the delivery follow-up with the provider itself (scheduled send),
        // so no in-app timer is needed and it can still be called off at the provider.
        var sendAt = DateTimeOffset.UtcNow.Add(_settings.FollowUpDelay);
        await NotifyAllAsync(order, NotificationKind.DeliveryFollowUp,
            $"eShop: How did the delivery of your order #{order.Id} go? We'd love to hear from you.",
            sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any follow-up the provider has not sent yet: a cancelled order
        // must never produce a "how did your delivery go" message.
        var pendingFollowUps = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var cancelled = await _messageProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (cancelled != null)
                {
                    followUp.SyncFromProvider(cancelled.Status ?? "canceled", cancelled.ErrorCode, cancelled.ErrorMessage);
                }
                else
                {
                    followUp.SyncFromProvider("canceled", null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up {MessageSid} for order {OrderId} at the provider.", followUp.ProviderMessageSid ?? "unknown", order.Id);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }

        await NotifyAllAsync(order, NotificationKind.OrderCancelled,
            $"eShop: Your order #{order.Id} has been cancelled. We're sorry for the inconvenience.", null, cancellationToken);
    }

    public async Task<OrderNotification> SendResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = original.Body ?? string.Empty;
        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.RecipientNumber,
            NotificationKind.Resend, body, null, idempotencyKey);
        await SendAndRecordAsync(resend, body, null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messageProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }
        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task SyncStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminal)
        {
            return;
        }

        try
        {
            var message = await _messageProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            if (message != null)
            {
                notification.SyncFromProvider(message.Status ?? notification.ProviderStatus, message.ErrorCode, message.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh status of provider message {MessageSid}.", notification.ProviderMessageSid);
        }
    }

    private async Task NotifyAllAsync(Order order, NotificationKind kind, string body, DateTimeOffset? scheduleAt, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.PhoneNumber, kind, body, scheduleAt);
            await SendAndRecordAsync(notification, body, scheduleAt, cancellationToken);
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, string body, DateTimeOffset? scheduleAt, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messageProvider.SendMessageAsync(notification.RecipientNumber, body, scheduleAt, cancellationToken);
            notification.MarkAccepted(message.Sid, message.Status ?? (scheduleAt.HasValue ? "scheduled" : "queued"));
            notification.SyncFromProvider(message.Status ?? notification.ProviderStatus, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            // Never log the recipient number.
            _logger.LogError(ex, "Failed to send {Kind} notification for order {OrderId}.", notification.Kind, notification.OrderId);
            notification.MarkSendFailed(ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}
