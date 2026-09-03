using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopOrderService : IShopOrderService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopOrderService> _logger;

    public ShopOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<ShopOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipTo, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidOperationException("An order must contain at least one item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);
        if (catalogItems.Count != ids.Length)
        {
            throw new InvalidOperationException("One or more catalog items were not found.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOperationException("Quantity must be greater than zero.");
            }

            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo ?? new Address("N/A", "N/A", "N/A", "USA", "00000");
        var order = new Order(buyerId, address, orderItems);
        order = await _orders.AddAsync(order, ct);

        await TryNotifyAsync(
            order,
            buyerId,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you!",
            sendAt: null,
            ct);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException("Order was not found.");

        order.MarkDispatched();
        await _orders.UpdateAsync(order, ct);

        await TryNotifyAsync(
            order,
            order.BuyerId,
            NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            ct);

        await TryNotifyAsync(
            order,
            order.BuyerId,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            ct);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException("Order was not found.");

        order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);

        await CancelScheduledFollowUpsAsync(order.Id, ct);

        await TryNotifyAsync(
            order,
            order.BuyerId,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            ct);

        return order;
    }

    public async Task<IReadOnlyList<ShopOrderDetail>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpec(buyerId), ct);
        await RefreshAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders.Select(o => new ShopOrderDetail(
            o,
            byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>())).ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), ct);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), ct);
        await RefreshAsync(notifications, ct);
        return notifications;
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpec(orderId), ct);
        foreach (var notification in scheduled)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var updated = await _smsGateway.CancelAsync(notification.ProviderSid, ct);
                notification.ApplyProviderState(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage, updated.Body);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up for order {OrderId} notification {NotificationId}.", orderId, notification.Id);
            }
        }
    }

    private async Task TryNotifyAsync(
        Order order,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken ct)
    {
        var destination = await GetActiveDestinationAsync(buyerId, ct);
        if (destination is null)
        {
            _logger.LogInformation("Skipping SMS for order {OrderId} kind {Kind}: no contact number on file.", order.Id, kind);
            return;
        }

        ProviderMessage result;
        try
        {
            result = await _smsGateway.SendAsync(destination, body, sendAt, ct);
        }
        catch (Exception)
        {
            _logger.LogWarning("SMS send threw for order {OrderId} kind {Kind}.", order.Id, kind);
            result = new ProviderMessage(false, null, "send_failed", null, null, null, destination, null, DateTimeOffset.UtcNow);
        }

        if (!result.Accepted)
        {
            _logger.LogWarning("SMS was not accepted for order {OrderId} kind {Kind} status {Status}.", order.Id, kind, result.Status);
        }

        var notification = new OrderNotification(
            order.Id,
            buyerId,
            kind,
            destination,
            result.Accepted ? body : body,
            result.Sid,
            result.Status,
            result.ErrorCode,
            result.ErrorMessage,
            sendAt);

        await _notifications.AddAsync(notification, ct);
    }

    private async Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(buyerId), ct);
        return numbers.FirstOrDefault()?.CanonicalNumber;
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderSid))
            {
                continue;
            }

            try
            {
                var latest = await _smsGateway.FetchAsync(notification.ProviderSid, ct);
                var body = notification.ContentRedacted ? notification.Body : latest.Body;
                notification.ApplyProviderState(latest.Sid, latest.Status, latest.ErrorCode, latest.ErrorMessage, body);
                if (notification.ContentRedacted)
                {
                    notification.MarkContentRedacted();
                }
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }
}
