using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsService smsService,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogItemQuantity> items, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }
        if (items.Any(i => i.Units <= 0))
        {
            throw new ArgumentException("Every item must have a quantity of at least one.", nameof(items));
        }

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), cancellationToken);
        var missingIds = items.Select(i => i.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.", nameof(items));
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Units);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed (total ${order.Total():0.00}). Thank you for shopping with us!",
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderDispatched,
            $"eShop: good news - your order #{order.Id} is on its way!",
            cancellationToken: cancellationToken);

        // The follow-up is queued with the provider itself; this application holds no timer.
        await NotifyBuyerAsync(order, NotificationType.DeliveryFollowUp,
            $"eShop: how did the delivery of your order #{order.Id} go? We would love to hear from you.",
            scheduleFor: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // A follow-up that has not yet gone out must never reach the shopper.
        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.",
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        foreach (var order in orders)
        {
            await RefreshNotificationStatesAsync(order.Id, cancellationToken);
        }
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string buyerId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || (!isAdmin && order.BuyerId != buyerId))
        {
            return null;
        }

        return await RefreshNotificationStatesAsync(orderId, cancellationToken);
    }

    public async Task<ResendNotificationResult> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notificationRepository.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpec(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendNotificationResult(existing, true, null);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendNotificationResult.Failed("Notification not found.");
        }
        if (original.ContentRedacted || original.Body is null)
        {
            return ResendNotificationResult.Failed("The content of this message has been disposed of and can no longer be sent.");
        }
        if (original.ContactNumberId is null)
        {
            return ResendNotificationResult.Failed("This message has no destination on file.");
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId.Value, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != original.BuyerId)
        {
            return ResendNotificationResult.Failed("The destination number is no longer registered; nothing may be sent to it.");
        }

        var send = await TrySendAsync(contactNumber.PhoneNumber, original.Body, cancellationToken);
        var resend = new OrderNotification(original.OrderId, original.BuyerId, contactNumber.Id, original.Type,
            original.Body, send.MessageSid, send.Status, send.ErrorCode, idempotencyKey: idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        return new ResendNotificationResult(resend, false, null);
    }

    public async Task<bool?> RedactNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (!notification.ContentRedacted && notification.ProviderMessageSid is not null)
        {
            // The text must be gone at the provider, not merely hidden here.
            var redacted = await _smsService.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (!redacted)
            {
                return false;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsService.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(new OrderNotificationsInRangeSpec(from, to), cancellationToken);

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count(n => n.ProviderMessageSid is not null)
        };

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.MessageSid, out var local))
            {
                report.Matched.Add(new ReconciliationMatch
                {
                    MessageSid = message.MessageSid,
                    NotificationId = local.Id,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status
                });
                localBySid.Remove(message.MessageSid);
            }
            else
            {
                report.MissingFromLocal.Add(new ReconciliationProviderMessage
                {
                    MessageSid = message.MessageSid,
                    To = message.To,
                    Status = message.Status,
                    DateSent = message.DateSent,
                    DateCreated = message.DateCreated
                });
            }
        }

        foreach (var leftover in localBySid.Values)
        {
            report.MissingFromProvider.Add(new ReconciliationLocalNotification
            {
                NotificationId = leftover.Id,
                MessageSid = leftover.ProviderMessageSid,
                Status = leftover.Status,
                CreatedAt = leftover.CreatedAt
            });
        }

        return report;
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in notifications.Where(n => n.Type == NotificationType.DeliveryFollowUp && n.Status == NotificationStatuses.Scheduled))
        {
            if (notification.ProviderMessageSid is null)
            {
                continue;
            }

            var cancelled = await _smsService.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
            if (cancelled)
            {
                notification.UpdateStatus(NotificationStatuses.Canceled, null);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Scheduled follow-up {NotificationId} for order {OrderId} could not be cancelled at the provider.", notification.Id, orderId);
            }
        }
    }

    private async Task<List<OrderNotification>> RefreshNotificationStatesAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _smsService.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (state is not null && state.Status != notification.Status)
                {
                    notification.UpdateStatus(state.Status, state.ErrorCode);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status of notification {NotificationId}; serving the last known state. {Error}", notification.Id, ex.Message);
            }
        }
        return notifications;
    }

    private static bool IsTerminal(string status)
        => status is NotificationStatuses.Delivered or NotificationStatuses.Undelivered
            or NotificationStatuses.Failed or NotificationStatuses.Canceled;

    /// <summary>
    /// Tells the shopper about an order event. Messaging trouble never fails the
    /// underlying operation; a shopper with no number on file is not messaged.
    /// </summary>
    private async Task NotifyBuyerAsync(Order order, NotificationType type, string body, DateTimeOffset? scheduleFor = null, CancellationToken cancellationToken = default)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
        var destination = contactNumbers.LastOrDefault();
        if (destination is null)
        {
            _logger.LogInformation("Order {OrderId}: buyer has no contact number on file; no {NotificationType} message sent.", order.Id, type);
            return;
        }

        SmsSendResult send;
        if (scheduleFor is null)
        {
            send = await TrySendAsync(destination.PhoneNumber, body, cancellationToken);
        }
        else
        {
            send = await TryScheduleAsync(destination.PhoneNumber, body, scheduleFor.Value, cancellationToken);
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, destination.Id, type,
            body, send.MessageSid, send.Status, send.ErrorCode, scheduledFor: scheduleFor);
        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task<SmsSendResult> TrySendAsync(string to, string body, CancellationToken cancellationToken)
    {
        try
        {
            return await _smsService.SendAsync(to, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SMS send failed before the provider accepted the message: {Error}", ex.Message);
            return new SmsSendResult(null, NotificationStatuses.Failed, null);
        }
    }

    private async Task<SmsSendResult> TryScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        try
        {
            return await _smsService.ScheduleAsync(to, body, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SMS scheduling failed before the provider accepted the message: {Error}", ex.Message);
            return new SmsSendResult(null, NotificationStatuses.Failed, null);
        }
    }
}
