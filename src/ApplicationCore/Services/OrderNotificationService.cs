using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did the delivery go?" follow-up is queued for.</summary>
    public static readonly TimeSpan DeliveryFeedbackDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<SmsNotification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<SmsNotification> notificationRepository,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? address, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new OrderItemsInvalidException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new OrderItemsInvalidException("Every order line must have a quantity greater than zero.");
        }

        // Sum quantities per catalog item so a repeated item id becomes one order line.
        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw OrderItemsInvalidException.ForMissingCatalogItems(missing);
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var shipToAddress = BuildAddress(address);
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Placed order {order.Id} for a shopper with {orderItems.Count} line(s).");

        await NotifyAsync(order, NotificationType.OrderPlaced,
            $"eShopOnWeb: thanks! Your order #{order.Id} has been placed.", cancellationToken);

        return order.Id;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} marked dispatched.");

        // Tell the shopper it is on its way...
        await NotifyAsync(order, NotificationType.OrderDispatched,
            $"eShopOnWeb: good news - your order #{order.Id} is on its way!", cancellationToken);

        // ...and queue a follow-up with the provider for a few days later asking how the delivery went.
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFeedbackDelay);
        await NotifyAsync(order, NotificationType.DeliveryFeedbackRequest,
            $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
            cancellationToken, scheduleFor: sendAt);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} marked cancelled.");

        // Critical first: call off any not-yet-sent follow-up so a "how did delivery go?" message never
        // reaches a customer whose order was cancelled.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyAsync(order, NotificationType.OrderCancelled,
            $"eShopOnWeb: your order #{order.Id} has been cancelled.", cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<SmsNotification>> GetNotificationsForOrdersAsync(string buyerId,
        IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (orderIds is null || orderIds.Count == 0)
        {
            return Array.Empty<SmsNotification>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new SmsNotificationsByBuyerSpecification(buyerId, orderIds), cancellationToken);
        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<SmsNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        // Orders are scoped to their owner: one shopper must never see another's.
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(
            new SmsNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    // Sends (or schedules) one message per contact number the shopper has on file, recording a
    // notification for each. A shopper with no number on file is simply not messaged, and a message that
    // cannot be sent never fails the underlying operation.
    private async Task NotifyAsync(Order order, NotificationType type, string body,
        CancellationToken cancellationToken, DateTimeOffset? scheduleFor = null)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);

        if (contactNumbers.Count == 0)
        {
            _logger.LogInformation($"Order {order.Id}: shopper has no number on file; no {type} message sent.");
            return;
        }

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new SmsNotification(order.BuyerId, order.Id, type, contactNumber.PhoneNumber, body,
                isScheduled: scheduleFor.HasValue);

            try
            {
                var sent = scheduleFor.HasValue
                    ? await _smsGateway.ScheduleAsync(contactNumber.PhoneNumber, body, scheduleFor.Value, cancellationToken)
                    : await _smsGateway.SendAsync(contactNumber.PhoneNumber, body, cancellationToken);

                notification.RecordProviderAccepted(sent.Sid, sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
            catch (SmsGatewayException ex)
            {
                // Never fail the order operation because a message could not be sent.
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning($"Order {order.Id}: {type} message could not be sent (provider error). It was recorded as failed.");
            }
            catch (Exception ex)
            {
                notification.RecordSendFailure(ex.Message);
                _logger.LogWarning($"Order {order.Id}: {type} message could not be sent. It was recorded as failed.");
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(
            new SmsNotificationsByOrderSpecification(orderId), cancellationToken);

        var pendingFollowUps = notifications.Where(n =>
            n.Type == NotificationType.DeliveryFeedbackRequest &&
            n.ProviderMessageSid is not null &&
            !SmsDeliveryStatus.IsTerminal(n.Status));

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation($"Order {orderId}: called off scheduled follow-up notification {followUp.Id}.");
            }
            catch (SmsGatewayException)
            {
                _logger.LogWarning($"Order {orderId}: could not call off scheduled follow-up notification {followUp.Id}.");
            }
        }
    }

    // For messages that are not yet in a terminal state, ask the provider for the latest outcome. There is
    // no webhook into this app, so status is obtained by asking the provider on read.
    private async Task RefreshDeliveryOutcomesAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || SmsDeliveryStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (state.Status != notification.Status || state.ErrorCode != notification.ErrorCode)
                {
                    notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (SmsGatewayException)
            {
                _logger.LogWarning($"Could not refresh delivery outcome for notification {notification.Id}.");
            }
        }
    }

    private static Address BuildAddress(ShippingAddressRequest? address)
    {
        if (address is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }

        return new Address(
            NullIfEmpty(address.Street) ?? "N/A",
            NullIfEmpty(address.City) ?? "N/A",
            NullIfEmpty(address.State) ?? "N/A",
            NullIfEmpty(address.Country) ?? "N/A",
            NullIfEmpty(address.ZipCode) ?? "00000");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
