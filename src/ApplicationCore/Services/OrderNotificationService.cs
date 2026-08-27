using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends order lifecycle SMS notifications via the configured messaging provider and
/// records each message (with its provider-owned state) so later requests can act on it.
/// All provider failures are swallowed after being recorded: a message that cannot be
/// sent must never fail the underlying order operation. Destination numbers are never
/// written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // The delivery follow-up is queued with the provider for a few days after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Thank you for shopping with us!";
        await SendAndRecordAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news - your order #{order.Id} has been dispatched and is on its way!";
        await SendAndRecordAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        await ScheduleAndRecordAsync(order, NotificationType.DeliveryFollowUp, followUpBody,
            DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A follow-up that has not yet gone out must never reach the shopper.
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Sorry for any inconvenience.";
        await SendAndRecordAsync(order, NotificationType.OrderCancelled, body, cancellationToken);
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || OrderNotificationStatuses.IsFinal(notification.Status))
            {
                continue;
            }

            try
            {
                var current = await _messagingProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (!string.Equals(current.Status, notification.Status, StringComparison.OrdinalIgnoreCase))
                {
                    notification.UpdateStatus(current.Status);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status of notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    private async Task SendAndRecordAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var contactNumber = await GetBuyerContactNumberAsync(order.BuyerId, cancellationToken);
        if (contactNumber is null)
        {
            _logger.LogInformation("Buyer {BuyerId} has no contact number on file; no {Type} notification sent for order {OrderId}",
                order.BuyerId, type, order.Id);
            return;
        }

        try
        {
            var result = await _messagingProvider.SendAsync(contactNumber.PhoneNumber, body, cancellationToken);
            await RecordAsync(order, contactNumber.Id, type, body, result.ProviderMessageSid, result.Status, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send {0} notification for order {1}: {2}", type, order.Id, ex.Message);
            await RecordAsync(order, contactNumber.Id, type, body, null, OrderNotificationStatuses.SendFailed, null, cancellationToken);
        }
    }

    private async Task ScheduleAndRecordAsync(Order order, NotificationType type, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var contactNumber = await GetBuyerContactNumberAsync(order.BuyerId, cancellationToken);
        if (contactNumber is null)
        {
            return;
        }

        try
        {
            var result = await _messagingProvider.ScheduleAsync(contactNumber.PhoneNumber, body, sendAt, cancellationToken);
            await RecordAsync(order, contactNumber.Id, type, body, result.ProviderMessageSid, result.Status, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to schedule {0} notification for order {1}: {2}", type, order.Id, ex.Message);
            await RecordAsync(order, contactNumber.Id, type, body, null, OrderNotificationStatuses.SendFailed, sendAt, cancellationToken);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);

        foreach (var notification in scheduled)
        {
            try
            {
                await _messagingProvider.CancelScheduledAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateStatus(OrderNotificationStatuses.Canceled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled notification {0}; refreshing its status. {1}", notification.Id, ex.Message);
                try
                {
                    var current = await _messagingProvider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                    notification.UpdateStatus(current.Status);
                }
                catch (Exception refreshEx)
                {
                    _logger.LogWarning("Could not refresh status of notification {0}: {1}", notification.Id, refreshEx.Message);
                }
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task<ContactNumber?> GetBuyerContactNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task RecordAsync(Order order, int contactNumberId, NotificationType type, string body,
        string? providerMessageSid, string status, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumberId, type, body,
            providerMessageSid, status, scheduledFor);
        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}

