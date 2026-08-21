using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderLifecycleService : IOrderLifecycleService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderLifecycleService> _logger;

    public OrderLifecycleService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderLifecycleService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> items,
        ShippingAddressDto? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CatalogOrderLine(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (grouped.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new ArgumentException("Each line must have a catalog item id and a quantity greater than zero.");
        }

        var catalogIds = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var missing = catalogIds.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new KeyNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var address = ToAddress(shippingAddress);
        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await NotifyQuietlyAsync(order, OrderNotificationKind.OrderPlaced, scheduleFollowUp: false, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderTransitionException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await NotifyQuietlyAsync(order, OrderNotificationKind.OrderDispatched, scheduleFollowUp: false, cancellationToken);
        await NotifyQuietlyAsync(order, OrderNotificationKind.DeliveryFollowUp, scheduleFollowUp: true, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderTransitionException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await NotifyQuietlyAsync(order, OrderNotificationKind.OrderCancelled, scheduleFollowUp: false, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        bool callerIsAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        if (!callerIsAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdsSpecification(ids), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }

    private async Task NotifyQuietlyAsync(
        Order order,
        OrderNotificationKind kind,
        bool scheduleFollowUp,
        CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (destinations.Count == 0)
            {
                return;
            }

            DateTimeOffset? sendAt = scheduleFollowUp ? DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay) : null;
            var body = OrderSmsTemplates.For(kind, order.Id);

            foreach (var destination in destinations)
            {
                var notification = new OrderNotification(
                    order.Id,
                    order.BuyerId,
                    destination.Id,
                    destination.CanonicalNumber,
                    kind,
                    body,
                    sendAt);

                try
                {
                    var snapshot = await _smsGateway.SendAsync(
                        new SmsSendRequest(destination.CanonicalNumber, body, sendAt),
                        cancellationToken);
                    notification.RecordProviderAccepted(snapshot.Sid, snapshot.Status, snapshot.DateSent);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}: {Message}", kind, order.Id, ex.Message);
                    notification.RecordSendFailure("failed", errorCode: null, errorMessage: "The provider rejected or could not accept the message.");
                }

                await _notifications.AddAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} succeeded but notifications could not be processed: {Message}", order.Id, ex.Message);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _notifications.ListAsync(new CancellableFollowUpsByOrderSpecification(orderId), cancellationToken);
            foreach (var followUp in pending)
            {
                try
                {
                    var latest = await _smsGateway.FetchAsync(followUp.ProviderMessageSid!, cancellationToken);
                    if (latest is not null)
                    {
                        followUp.ApplyProviderState(latest.Status, latest.ErrorCode, latest.ErrorMessage, latest.DateSent);
                    }

                    if (!followUp.IsCancellableFollowUp())
                    {
                        await _notifications.UpdateAsync(followUp, cancellationToken);
                        continue;
                    }

                    var cancelled = await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                    if (cancelled is not null)
                    {
                        followUp.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.DateSent);
                    }

                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not cancel follow-up notification {NotificationId} for order {OrderId}: {Message}", followUp.Id, orderId, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed while cancelling follow-up messages for order {OrderId}: {Message}", orderId, ex.Message);
        }
    }

    private async Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.DateSent);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }
    }

    private static Address ToAddress(ShippingAddressDto? dto)
    {
        if (dto is null ||
            string.IsNullOrWhiteSpace(dto.Street) ||
            string.IsNullOrWhiteSpace(dto.City) ||
            string.IsNullOrWhiteSpace(dto.Country) ||
            string.IsNullOrWhiteSpace(dto.ZipCode))
        {
            return new Address("123 Main St.", "Kent", "OH", "USA", "44240");
        }

        return new Address(dto.Street, dto.City, dto.State ?? string.Empty, dto.Country, dto.ZipCode);
    }
}
