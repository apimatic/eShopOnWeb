using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How long after dispatch the delivery follow-up goes out. Must be within
    // the provider's scheduling window (15 minutes to 35 days for Twilio).
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<NotificationResendRecord> _resendRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<NotificationResendRecord> resendRepository,
        ISmsService smsService,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _resendRepository = resendRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Total: ${order.Total():0.00}. Thank you for shopping with us!";
        return SendToShopperAsync(order, OrderNotificationType.OrderPlaced, body);
    }

    public async Task NotifyOrderDispatchedAsync(Order order)
    {
        var body = $"eShop: good news! Your order #{order.Id} is on its way.";
        await SendToShopperAsync(order, OrderNotificationType.OrderDispatched, body);

        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        await SendToShopperAsync(order, OrderNotificationType.DeliveryFollowUp, followUpBody, followUpAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order)
    {
        // Call off any follow-up that has not yet gone out first, so it can
        // never reach the shopper after the cancellation.
        var pendingFollowUps = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id));
        foreach (var followUp in pendingFollowUps)
        {
            if (followUp.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                var cancelled = await _smsService.CancelScheduledMessageAsync(followUp.ProviderMessageSid);
                followUp.UpdateProviderStatus(cancelled ? "canceled" : followUp.Status);
                await _notificationRepository.UpdateAsync(followUp);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {0} for order {1}", followUp.ProviderMessageSid, order.Id);
            }
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendToShopperAsync(order, OrderNotificationType.OrderCancelled, body);
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId));

        await RefreshStatusesAsync(notifications);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderSummary
            {
                Order = o,
                Notifications = notifications.Where(n => n.OrderId == o.Id).ToList()
            })
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string callerId, bool isOperator)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null || (!isOperator && order.BuyerId != callerId))
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        await RefreshStatusesAsync(notifications);
        return notifications;
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, string operatorId)
    {
        var existingRecord = await _resendRepository.FirstOrDefaultAsync(new NotificationResendByKeySpecification(idempotencyKey));
        if (existingRecord != null)
        {
            return await _notificationRepository.GetByIdAsync(existingRecord.ResultNotificationId);
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId);
        if (source == null)
        {
            return null;
        }

        if (source.ContentDisposed || source.Body is null)
        {
            throw new InvalidOperationException("The content of this message has been disposed of and can no longer be sent.");
        }

        if (source.ContactNumberId is null)
        {
            throw new InvalidOperationException("This message has no recipient on file to resend to.");
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(source.ContactNumberId.Value);
        if (contactNumber == null)
        {
            throw new InvalidOperationException("The recipient's number is no longer on file; nothing may be sent to it again.");
        }

        var resend = new OrderNotification(source.OrderId, source.BuyerId, contactNumber.Id, OrderNotificationType.Resend, source.Body);
        try
        {
            var result = await _smsService.SendMessageAsync(contactNumber.PhoneNumber, source.Body);
            if (result.Success)
            {
                resend.MarkSent(result.MessageSid!, result.Status ?? "queued");
            }
            else
            {
                resend.MarkFailed(result.ErrorMessage ?? "The provider rejected the message.");
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to resend notification {0}", notificationId);
            resend.MarkFailed("The provider could not be reached.");
        }

        resend = await _notificationRepository.AddAsync(resend);
        await _resendRepository.AddAsync(new NotificationResendRecord(idempotencyKey, operatorId, notificationId, resend.Id));
        return resend;
    }

    public async Task<OrderNotification?> DisposeContentAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification == null)
        {
            return null;
        }

        if (notification.ContentDisposed)
        {
            return notification;
        }

        if (notification.ProviderMessageSid != null)
        {
            // Redact at the provider first; only clear the local copy once the
            // provider's copy is gone, so a failure leaves both intact for retry.
            var redacted = await _smsService.RedactMessageBodyAsync(notification.ProviderMessageSid);
            if (!redacted)
            {
                throw new InvalidOperationException("The provider could not dispose of the message content; nothing was changed locally.");
            }
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification);
        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var providerMessages = await _smsService.ListMessagesAsync(from, to);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsCreatedInRangeSpecification(from, to));

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport { From = from, To = to };

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                report.Matched.Add(new ReconciledMessage
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status
                });
                localBySid.Remove(message.Sid);
            }
            else
            {
                report.ProviderOnly.Add(new ReconciledMessage
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent
                });
            }
        }

        foreach (var remaining in localBySid.Values)
        {
            report.EshopOnly.Add(new ReconciledMessage
            {
                ProviderMessageSid = remaining.ProviderMessageSid,
                NotificationId = remaining.Id,
                OrderId = remaining.OrderId,
                LocalStatus = remaining.Status
            });
        }

        return report;
    }

    private async Task SendToShopperAsync(Order order, OrderNotificationType type, string body, DateTimeOffset? sendAt = null)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId));
        var contactNumber = contactNumbers.FirstOrDefault();
        if (contactNumber == null)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, type, body, sendAt);
        try
        {
            var result = await _smsService.SendMessageAsync(contactNumber.PhoneNumber, body, sendAt);
            if (result.Success)
            {
                notification.MarkSent(result.MessageSid!, result.Status ?? "queued");
            }
            else
            {
                notification.MarkFailed(result.ErrorMessage ?? "The provider rejected the message.");
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to send {0} notification for order {1}", type, order.Id);
            notification.MarkFailed("The provider could not be reached.");
        }

        await _notificationRepository.AddAsync(notification);
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications)
    {
        foreach (var notification in notifications.Where(n => n.ProviderMessageSid != null && !n.HasTerminalStatus()))
        {
            try
            {
                var record = await _smsService.GetMessageAsync(notification.ProviderMessageSid!);
                if (record?.Status != null && record.Status != notification.Status)
                {
                    notification.UpdateProviderStatus(record.Status);
                    await _notificationRepository.UpdateAsync(notification);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh status for message {0}", notification.ProviderMessageSid);
            }
        }
    }
}
