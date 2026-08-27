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
    /// <summary>How long after dispatch the provider should send the delivery follow-up.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total: ${order.Total():0.00}. Thank you for shopping with us!";
        await SendToShopperAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news! Your order #{order.Id} has been dispatched and is on its way.";
        var contactNumber = await SendToShopperAsync(order, NotificationType.OrderDispatched, body, cancellationToken);
        if (contactNumber is null)
        {
            return;
        }

        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go? Reply and let us know!";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id,
            NotificationType.DeliveryFollowUp, followUpBody, scheduledFor: sendAt);
        await PersistProviderOutcomeAsync(notification,
            ct => _smsProvider.ScheduleMessageAsync(contactNumber.PhoneNumber, followUpBody, sendAt, ct),
            order.Id, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, cancellationToken);

        // A queued delivery follow-up must never reach a shopper whose order was cancelled.
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in notifications.Where(n =>
                     n.Type == NotificationType.DeliveryFollowUp &&
                     n.ProviderMessageSid != null &&
                     n.Status == "scheduled"))
        {
            try
            {
                var result = await _smsProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (result is not null)
                {
                    followUp.UpdateProviderStatus(result.Status ?? "canceled", result.ErrorCode, result.ErrorMessage);
                }
                else
                {
                    followUp.UpdateProviderStatus("cancel-failed", null, "Provider did not confirm cancellation.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {0} for order {1}: {2}", followUp.Id, order.Id, ex.Message);
                followUp.UpdateProviderStatus("cancel-failed", null, ex.Message);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    private async Task<ContactNumber?> SendToShopperAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        var contactNumber = contactNumbers.FirstOrDefault();
        if (contactNumber is null)
        {
            // A shopper with no number on file is simply not messaged.
            return null;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, type, body);
        await PersistProviderOutcomeAsync(notification,
            ct => _smsProvider.SendMessageAsync(contactNumber.PhoneNumber, body, ct),
            order.Id, cancellationToken);
        return contactNumber;
    }

    private async Task PersistProviderOutcomeAsync(
        OrderNotification notification,
        Func<CancellationToken, Task<ProviderMessageResult>> providerCall,
        int orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await providerCall(cancellationToken);
            if (result.Success && result.MessageSid is not null)
            {
                notification.MarkProviderAccepted(result.MessageSid, result.Status ?? "accepted");
            }
            else
            {
                notification.MarkSendFailed(result.Status ?? "failed", result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Failed to send {0} notification for order {1}: {2}", notification.Type, orderId, ex.Message);
            notification.MarkSendFailed("error", null, ex.Message);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}
