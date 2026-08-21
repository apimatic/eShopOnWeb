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

public class OrderPlacementService : IOrderPlacementService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);
    private static readonly Address DefaultShipTo = new("123 Demo Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<ShopperContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly IMessagingProviderClient _messaging;
    private readonly IAppLogger<OrderPlacementService> _logger;

    public OrderPlacementService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<ShopperContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        IMessagingProviderClient messaging,
        IAppLogger<OrderPlacementService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Each item quantity must be greater than zero.", nameof(items));
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.", nameof(items));
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            scheduleAt: null,
            cancellationToken);

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduleAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of your eShopOnWeb order #{order.Id} go?",
            scheduleAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await CancelScheduledFollowUpsAsync(order.Id, cancellationToken);

        await TryNotifyAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduleAt: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ShopperOrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerIdSpecification(buyerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => new ShopperOrderDto(
                order.Id,
                order.Status.ToString(),
                order.Total(),
                order.OrderDate,
                notifications.Where(n => n.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task CancelScheduledFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpByOrderIdSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                var updated = await _messaging.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                ApplyProviderState(followUp, updated);
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Cancelled scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    private async Task TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var destination = await ResolveActiveDestinationAsync(order.BuyerId, cancellationToken);
            if (destination is null)
            {
                _logger.LogInformation("Skipping {Kind} notification for order {OrderId}; shopper has no number on file.", kind, order.Id);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, kind, destination, body);
            if (scheduleAt.HasValue)
            {
                notification.RecordScheduled(scheduleAt.Value);
            }

            await _notifications.AddAsync(notification, cancellationToken);

            try
            {
                var providerMessage = scheduleAt.HasValue
                    ? await _messaging.ScheduleAsync(destination, body, scheduleAt.Value, cancellationToken)
                    : await _messaging.SendAsync(destination, body, cancellationToken);

                ApplyProviderState(notification, providerMessage);
            }
            catch (Exception)
            {
                notification.RecordLocalFailure("The messaging provider did not accept the message.");
                _logger.LogWarning("Provider rejected {Kind} notification {NotificationId} for order {OrderId}.", kind, notification.Id, order.Id);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} could not be sent; the order operation still succeeded.", kind, order.Id);
        }
    }

    private async Task<string?> ResolveActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ShopperContactNumbersSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault()?.PhoneNumber;
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications.Where(n => !string.IsNullOrEmpty(n.ProviderSid)))
        {
            try
            {
                var latest = await _messaging.FetchAsync(notification.ProviderSid!, cancellationToken);
                ApplyProviderState(notification, latest);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    private static void ApplyProviderState(OrderNotification notification, ProviderMessage message)
    {
        if (string.IsNullOrEmpty(notification.ProviderSid))
        {
            notification.RecordProviderAcceptance(message.Sid, message.Status, message.DateSent);
            return;
        }

        notification.ApplyProviderOutcome(message.Status, message.ErrorCode, SanitizeProviderError(message.ErrorCode), message.DateSent);
    }

    private static string? SanitizeProviderError(int? errorCode)
        => errorCode is null ? null : $"Provider error {errorCode}";
}
