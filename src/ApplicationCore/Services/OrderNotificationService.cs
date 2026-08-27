using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up is queued with the provider this long after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: thank you! Your order #{order.Id} has been placed. Total: {order.Total():C}.";
        return NotifyAllContactNumbersAsync(order, NotificationKind.OrderPlaced, body, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news! Your order #{order.Id} is on its way.";
        await NotifyAllContactNumbersAsync(order, NotificationKind.OrderDispatched, body, null, cancellationToken);

        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAllContactNumbersAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A follow-up that has not yet gone out must never reach a shopper whose order was cancelled.
        await CancelPendingFollowUpsAsync(order, cancellationToken);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Contact support if this is unexpected.";
        await NotifyAllContactNumbersAsync(order, NotificationKind.OrderCancelled, body, null, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.Kind == NotificationKind.DeliveryFollowUp && n.IsScheduled))
        {
            try
            {
                if (followUp.ProviderMessageSid is not null)
                {
                    var state = await _smsGateway.CancelScheduledMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                    followUp.ApplyProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                }
                else
                {
                    followUp.MarkSendFailed("Cancelled before the provider accepted the message.");
                }
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}: {Error}", followUp.Id, order.Id, ex.Message);
            }
        }
    }

    private async Task NotifyAllContactNumbersAsync(Order order, NotificationKind kind, string body,
        DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (contactNumbers.Count == 0)
            {
                return; // A shopper with no number on file is simply not messaged.
            }

            foreach (var contactNumber in contactNumbers)
            {
                await SendAndRecordAsync(order, contactNumber, kind, body, sendAt, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Messaging must never fail the underlying operation.
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}: {Error}", kind, order.Id, ex.Message);
        }
    }

    private async Task SendAndRecordAsync(Order order, ContactNumber contactNumber, NotificationKind kind,
        string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.PhoneNumber, kind, body, sendAt);
        try
        {
            var result = await _smsGateway.SendMessageAsync(contactNumber.PhoneNumber, body, sendAt, cancellationToken);
            if (result.Accepted && result.MessageSid is not null)
            {
                notification.MarkAccepted(result.MessageSid, result.Status ?? (sendAt.HasValue ? "scheduled" : "queued"));
            }
            else
            {
                notification.MarkSendFailed(result.ErrorMessage ?? "The provider rejected the message.", result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SMS send failed for order {OrderId} notification kind {Kind}: {Error}", order.Id, kind, ex.Message);
            notification.MarkSendFailed("The messaging provider could not be reached.");
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}
