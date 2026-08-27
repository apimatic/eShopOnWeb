using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the delivery follow-up is queued for.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private const string SendFailedStatus = "send-failed";

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IMessagingClient messagingClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: thanks for your order #{order.Id}! We'll text you when it's on its way.";
        await SendToShopperAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} is on its way!";
        await SendToShopperAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUpBody = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love to know.";
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            OrderNotification record;
            try
            {
                var message = await _messagingClient.ScheduleMessageAsync(number.PhoneNumber, followUpBody, sendAt, cancellationToken);
                record = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp,
                    number.PhoneNumber, followUpBody, message.Sid, message.Status ?? "unknown", sendAt);
            }
            catch (Exception ex)
            {
                LogProviderFailure(ex, NotificationType.DeliveryFollowUp, order.Id);
                record = FailedRecord(order, NotificationType.DeliveryFollowUp, number.PhoneNumber, followUpBody, ex, sendAt);
            }
            await _notifications.AddAsync(record, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any follow-up that has not gone out yet: a cancelled order must
        // never produce a "how was your delivery" message.
        var pendingFollowUps = await _notifications.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var message = await _messagingClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderState(message.Status ?? "canceled", message.ErrorCode, message.ErrorMessage);
            }
            catch (Exception ex)
            {
                LogProviderFailure(ex, NotificationType.DeliveryFollowUp, order.Id);
            }
            await _notifications.UpdateAsync(followUp, cancellationToken);
        }

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, cancellationToken);
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var message = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderState(message.Status ?? notification.Status, message.ErrorCode, message.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            LogProviderFailure(ex, notification.Type, notification.OrderId);
        }
    }

    private async Task SendToShopperAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        foreach (var number in numbers)
        {
            OrderNotification record;
            try
            {
                var message = await _messagingClient.SendMessageAsync(number.PhoneNumber, body, cancellationToken);
                record = new OrderNotification(order.Id, order.BuyerId, type,
                    number.PhoneNumber, body, message.Sid, message.Status ?? "unknown");
                record.UpdateProviderState(message.Status ?? "unknown", message.ErrorCode, message.ErrorMessage);
            }
            catch (Exception ex)
            {
                LogProviderFailure(ex, type, order.Id);
                record = FailedRecord(order, type, number.PhoneNumber, body, ex);
            }
            await _notifications.AddAsync(record, cancellationToken);
        }
    }

    private static OrderNotification FailedRecord(Order order, NotificationType type, string destination,
        string body, Exception ex, DateTimeOffset? scheduledFor = null)
    {
        var record = new OrderNotification(order.Id, order.BuyerId, type, destination, body,
            providerMessageSid: null, SendFailedStatus, scheduledFor);
        record.UpdateProviderState(SendFailedStatus, null, ex.GetType().Name);
        return record;
    }

    // Never log destination numbers or message bodies; provider error messages can embed them.
    private void LogProviderFailure(Exception ex, NotificationType type, int orderId)
    {
        if (ex is MessagingProviderException mpex)
        {
            _logger.LogError("Provider call failed for {NotificationType} on order {OrderId}: HTTP {HttpStatus}, provider error {ProviderErrorCode}",
                type, orderId, mpex.HttpStatusCode?.ToString() ?? "n/a", mpex.ProviderErrorCode?.ToString() ?? "n/a");
        }
        else
        {
            _logger.LogError("Provider call failed for {NotificationType} on order {OrderId}: {ExceptionType}",
                type, orderId, ex.GetType().Name);
        }
    }
}
