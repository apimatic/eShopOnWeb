using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private const int MaxStatusSyncsPerRequest = 10;

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Order> orderRepository,
        ISmsService smsService,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _orderRepository = orderRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total: ${order.Total()}. Thank you for shopping with us.";
        await SendAndRecordAsync(order, NotificationType.OrderPlaced, body, null, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var dispatchedBody = $"eShopOnWeb: good news — your order #{order.Id} is on its way.";
        await SendAndRecordAsync(order, NotificationType.OrderDispatched, dispatchedBody, null, ct);

        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await ScheduleAndRecordAsync(order, followUpBody, sendAt, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendAndRecordAsync(order, NotificationType.OrderCancelled, body, null, ct);

        var scheduledFollowUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(order.Id), ct);
        foreach (var followUp in scheduledFollowUps)
        {
            try
            {
                await _smsService.CancelScheduledMessageAsync(followUp.MessageSid!, ct);
                followUp.UpdateStatus(MessageStatuses.Canceled, null, null);
                await _notificationRepository.UpdateAsync(followUp, ct);
                _logger.LogInformation("Cancelled scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, order.Id);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} (sid {MessageSid}) for order {OrderId}: provider status {StatusCode}.",
                    followUp.Id, followUp.MessageSid, order.Id, ex.StatusCode?.ToString() ?? "none");
            }
        }
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), ct);
        await SyncStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task SyncRecentStatusesAsync(string buyerId, CancellationToken ct = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByBuyerSpecification(buyerId), ct);
        var candidates = notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(MaxStatusSyncsPerRequest)
            .ToList();
        await SyncStatusesAsync(candidates, ct);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetBuyerNotificationsAsync(string buyerId, CancellationToken ct = default)
    {
        await SyncRecentStatusesAsync(buyerId, ct);
        return await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default)
    {
        await SyncStatusesAsync(new[] { notification }, ct);
    }

    public async Task<OrderNotification> ResendAsync(OrderNotification source, string idempotencyKey, CancellationToken ct = default)
    {
        var sent = await _smsService.SendMessageAsync(source.ToNumber, source.Body!, ct);
        var resend = new OrderNotification(source.OrderId, source.BuyerId, source.ToNumber,
            NotificationType.Resend, sent.Sid, source.Body, sent.Status, idempotencyKey: idempotencyKey);
        await _notificationRepository.AddAsync(resend, ct);
        _logger.LogInformation("Resent notification {SourceNotificationId} as {NotificationId} (sid {MessageSid}).",
            source.Id, resend.Id, sent.Sid);
        return resend;
    }

    public async Task RedactContentAsync(OrderNotification notification, CancellationToken ct = default)
    {
        if (notification.MessageSid != null)
        {
            await _smsService.RedactMessageBodyAsync(notification.MessageSid, ct);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Redacted content of notification {NotificationId} (sid {MessageSid}).",
            notification.Id, notification.MessageSid);
    }

    private async Task SendAndRecordAsync(Order order, NotificationType type, string body, string? idempotencyKey, CancellationToken ct)
    {
        var destination = await GetDestinationNumberAsync(order.BuyerId, ct);
        if (destination == null)
        {
            return;
        }

        try
        {
            var sent = await _smsService.SendMessageAsync(destination, body, ct);
            await RecordAsync(order, type, destination, body, sent.Sid, sent.Status, null, idempotencyKey, ct);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Order {OrderId} {NotificationType} message was not accepted by the provider: status {StatusCode}.",
                order.Id, type, ex.StatusCode?.ToString() ?? "none");
            await RecordAsync(order, type, destination, body, null, MessageStatuses.Failed, null, idempotencyKey, ct);
        }
    }

    private async Task ScheduleAndRecordAsync(Order order, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var destination = await GetDestinationNumberAsync(order.BuyerId, ct);
        if (destination == null)
        {
            return;
        }

        try
        {
            var scheduled = await _smsService.ScheduleMessageAsync(destination, body, sendAt, ct);
            await RecordAsync(order, NotificationType.DeliveryFollowUp, destination, body,
                scheduled.Sid, scheduled.Status, sendAt, null, ct);
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Order {OrderId} follow-up could not be scheduled: provider status {StatusCode}.",
                order.Id, ex.StatusCode?.ToString() ?? "none");
            await RecordAsync(order, NotificationType.DeliveryFollowUp, destination, body,
                null, MessageStatuses.Failed, sendAt, null, ct);
        }
    }

    private async Task RecordAsync(Order order, NotificationType type, string toNumber, string body,
        string? messageSid, string status, DateTimeOffset? scheduledFor, string? idempotencyKey, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, toNumber, type,
            messageSid, body, status, scheduledFor, idempotencyKey);
        await _notificationRepository.AddAsync(notification, ct);
    }

    private async Task SyncStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications.Where(n => n.MessageSid != null && !MessageStatuses.IsTerminal(n.LastKnownStatus)))
        {
            try
            {
                var current = await _smsService.GetMessageAsync(notification.MessageSid!, ct);
                notification.UpdateStatus(current.Status ?? notification.LastKnownStatus,
                    current.ErrorCode, current.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not refresh status of notification {NotificationId} (sid {MessageSid}): provider status {StatusCode}.",
                    notification.Id, notification.MessageSid, ex.StatusCode?.ToString() ?? "none");
            }
        }
    }

    private async Task<string?> GetDestinationNumberAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return numbers.Count == 0 ? null : numbers[^1].PhoneNumber;
    }
}
