using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFlowService : IOrderFlowService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderFlowService> _logger;

    public OrderFlowService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        ISmsGateway smsGateway,
        IAppLogger<OrderFlowService> logger)
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
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Each item quantity must be greater than zero.", nameof(lines));
        }

        var catalogIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.", nameof(lines));
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, OrderNotificationKind.OrderPlaced, sendAt: null, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, OrderNotificationKind.OrderDispatched, sendAt: null, cancellationToken);
        var followUpAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await NotifyBuyerAsync(order, OrderNotificationKind.DeliveryFollowUp, followUpAt, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        await NotifyBuyerAsync(order, OrderNotificationKind.OrderCancelled, sendAt: null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<BuyerOrder>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<BuyerOrder>();
        }

        var notifications = await _notifications.ListAsync(
            new NotificationsByOrderIdsSpecification(orders.Select(o => o.Id)),
            cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());
        return orders
            .Select(o => new BuyerOrder(o, byOrder.TryGetValue(o.Id, out var list) ? list : Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task<Order> RequireOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }

    private async Task NotifyBuyerAsync(
        Order order,
        OrderNotificationKind kind,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContactNumber> numbers;
        try
        {
            numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load contact numbers while sending an order notification. {Message}", ex.Message);
            return;
        }

        if (numbers.Count == 0)
        {
            return;
        }

        var body = OrderNotificationTemplates.BodyFor(kind, order.Id);
        foreach (var number in numbers)
        {
            await TrySendAsync(order, kind, number.CanonicalNumber, body, sendAt, originalNotificationId: null, idempotencyKey: null, cancellationToken);
        }
    }

    internal async Task<OrderNotification> TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string destinationNumber,
        string body,
        DateTimeOffset? sendAt,
        int? originalNotificationId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ProviderMessage? provider = null;
        try
        {
            provider = await _smsGateway.SendAsync(new SendMessageRequest(destinationNumber, body, sendAt), cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Order notification {Kind} for order {OrderId} could not be handed to the provider. The order operation continues.", kind, order.Id);
        }

        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            destinationNumber,
            body,
            provider?.Sid,
            provider?.Status ?? "send_failed",
            provider?.ErrorCode,
            sendAt,
            provider?.DateSent,
            originalNotificationId,
            idempotencyKey);

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        foreach (var followUp in notifications.Where(n => n.IsCancellableScheduledMessage()))
        {
            try
            {
                var updated = await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.ApplyProviderState(updated.Sid, updated.Status, updated.ErrorCode, updated.Body, updated.DateSent, contentRedacted: followUp.ContentRedacted);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not cancel a scheduled follow-up for order {OrderId}. The cancel operation continues.", orderId);
            }
        }
    }

    private async Task RefreshFromProviderAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var latest = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                var providerRedacted = latest.Body == string.Empty;
                notification.ApplyProviderState(
                    latest.Sid,
                    latest.Status,
                    latest.ErrorCode,
                    latest.Body,
                    latest.DateSent,
                    contentRedacted: notification.ContentRedacted || providerRedacted);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for a notification on order {OrderId}. Returning last known state.", notification.OrderId);
            }
        }
    }
}
