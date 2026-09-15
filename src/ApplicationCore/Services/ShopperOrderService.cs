using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ShopperContactNumber> _contactRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsNotificationGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ShopperContactNumber> contactRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsNotificationGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<ShopperOrderService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactRepository = contactRepository;
        _notificationRepository = notificationRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            return AppResult.Invalid<Order>("At least one catalog item is required.");
        }

        if (lines.Any(l => l.Quantity <= 0 || l.CatalogItemId <= 0))
        {
            return AppResult.Invalid<Order>("Each line must have a catalog item id and a quantity greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            return AppResult.Invalid<Order>("One or more catalog items were not found.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyAsync(order, NotificationKinds.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Total: {order.Total():0.00}.",
            sendAt: null, cancellationToken);

        return Result<Order>.Success(order);
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);

        foreach (var notification in notifications)
        {
            await RefreshStatusAsync(notification, cancellationToken);
        }

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => new ShopperOrderSummary(
                order,
                notifications.Where(n => n.OrderId == order.Id).OrderBy(n => n.CreatedAt).ToList()))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<OrderNotification>>> ListNotificationsAsync(string buyerId, int orderId, bool isOperator, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        if (!isOperator && order.BuyerId != buyerId)
        {
            return Result<IReadOnlyList<OrderNotification>>.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshStatusAsync(notification, cancellationToken);
        }

        return Result<IReadOnlyList<OrderNotification>>.Success(notifications);
    }

    internal async Task NotifyAsync(Order order, string kind, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var destinations = await _contactRepository.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
        if (destinations.Count == 0)
        {
            _logger.LogInformation("No contact number on file for order {OrderId}; skipping {Kind} notification.", order.Id, kind);
            return;
        }

        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination.CanonicalNumber, body, sendAt);
            await _notificationRepository.AddAsync(notification, cancellationToken);

            var snapshot = await _gateway.TrySendAsync(new SmsSendRequest(destination.CanonicalNumber, body, sendAt), cancellationToken);
            if (string.IsNullOrEmpty(snapshot.Sid))
            {
                notification.MarkSendFailed(snapshot.ErrorMessage ?? "The message could not be sent.");
                _logger.LogWarning("Notification {NotificationId} for order {OrderId} kind {Kind} did not obtain a provider identifier.", notification.Id, order.Id, kind);
            }
            else
            {
                notification.ApplyProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage, snapshot.Direction);
                _logger.LogInformation("Notification {NotificationId} for order {OrderId} kind {Kind} submitted as {ProviderSid} with status {Status}.", notification.Id, order.Id, kind, snapshot.Sid ?? string.Empty, snapshot.Status ?? string.Empty);
            }

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    internal async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            return;
        }

        var snapshot = await _gateway.FetchAsync(notification.ProviderSid, cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        notification.ApplyProviderResult(
            snapshot.Sid ?? notification.ProviderSid,
            snapshot.Status,
            snapshot.ErrorCode,
            snapshot.ErrorMessage,
            snapshot.Direction,
            notification.ContentDisposed ? null : snapshot.Body);

        if (notification.ContentDisposed)
        {
            notification.DisposeContent();
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }
}
