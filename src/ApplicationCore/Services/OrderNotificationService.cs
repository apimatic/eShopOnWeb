using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends and records the SMS messages that go out as an order moves. Everything here is best-effort:
/// a message that cannot be sent is recorded as failed and never surfaced to the caller, so the order
/// operation it accompanies always succeeds. A shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did delivery go" follow-up is queued for.</summary>
    private const int FollowUpDelayDays = 3;

    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsSender _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IReadRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ISmsSender sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var number in await GetNumbersAsync(order.BuyerId))
            {
                await SendAndRecordAsync(order.BuyerId, order.Id, NotificationKind.OrderPlaced,
                    number.PhoneNumber, OrderNotificationMessages.Placed(order), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order-placed notifications failed for order {0}: {1}", order.Id, ex.GetType().Name);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var number in await GetNumbersAsync(order.BuyerId))
            {
                await SendAndRecordAsync(order.BuyerId, order.Id, NotificationKind.OrderDispatched,
                    number.PhoneNumber, OrderNotificationMessages.Dispatched(order), cancellationToken);

                await ScheduleFollowUpAsync(order, number.PhoneNumber, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order-dispatched notifications failed for order {0}: {1}", order.Id, ex.GetType().Name);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Tell the shopper (best effort) ...
        try
        {
            foreach (var number in await GetNumbersAsync(order.BuyerId))
            {
                await SendAndRecordAsync(order.BuyerId, order.Id, NotificationKind.OrderCancelled,
                    number.PhoneNumber, OrderNotificationMessages.Cancelled(order), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order-cancelled notification failed for order {0}: {1}", order.Id, ex.GetType().Name);
        }

        // ... and, independently, call off any follow-up that has not yet gone out. This is the part
        // that must not be skipped: a cancelled order must never get a "how did delivery go?" message.
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
    }

    public async Task RefreshDeliveryStateAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid) || NotificationStatus.IsTerminal(notification.Status))
        {
            return;
        }

        try
        {
            var result = await _sms.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.UpdateDeliveryState(result.Status, result.ErrorCode, result.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh delivery state for notification {0}: {1}", notification.Id, ex.GetType().Name);
        }
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
            foreach (var followUp in followUps)
            {
                try
                {
                    await _sms.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.MarkCanceled();
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to cancel scheduled follow-up notification {0}: {1}", followUp.Id, ex.GetType().Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not enumerate scheduled follow-ups for order {0}: {1}", orderId, ex.GetType().Name);
        }
    }

    private async Task<Notification> SendAndRecordAsync(
        string buyerId, int orderId, NotificationKind kind, string toNumber, string body, CancellationToken cancellationToken)
    {
        var notification = new Notification(buyerId, orderId, kind, toNumber, body);
        await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _sms.SendAsync(toNumber, body, cancellationToken);
            notification.ApplyProviderResult(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed("Provider send failed.");
            _logger.LogWarning("SMS send failed for notification {0} (kind {1}): {2}", notification.Id, kind, ex.GetType().Name);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private async Task ScheduleFollowUpAsync(Order order, string toNumber, CancellationToken cancellationToken)
    {
        var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);
        var notification = new Notification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp,
            toNumber, OrderNotificationMessages.DeliveryFollowUp(order), scheduledSendAt: sendAt);
        await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _sms.ScheduleAsync(toNumber, notification.Body!, sendAt, cancellationToken);
            notification.ApplyProviderResult(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed("Provider schedule failed.");
            _logger.LogWarning("Follow-up scheduling failed for notification {0} (order {1}): {2}", notification.Id, order.Id, ex.GetType().Name);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    private async Task<IReadOnlyList<ContactNumber>> GetNumbersAsync(string buyerId) =>
        await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
}
