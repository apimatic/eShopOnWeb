using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders from catalog items and moves them through their lifecycle, triggering the matching
/// shopper notifications as each action happens. Reuses the app's existing Order/OrderItem model.
/// </summary>
public interface IOrderProcessingService
{
    /// <summary>Place an order for a shopper from catalog item ids and quantities, and tell them it was placed.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Mark an order dispatched (operator action) and notify the shopper.</summary>
    Task<OrderOperationResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Mark an order cancelled (operator action) and notify the shopper.</summary>
    Task<OrderOperationResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each with its notifications and their current delivery outcomes.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>The notifications for a specific order, scoped to the caller who owns it.</summary>
    Task<OrderNotificationsView> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

public record PlaceOrderResult(bool Success, Order? Order, string? Error)
{
    public static PlaceOrderResult Ok(Order order) => new(true, order, null);
    public static PlaceOrderResult Rejected(string error) => new(false, null, error);
}

public record OrderOperationResult(bool Found, Order? Order, string? Error)
{
    public static OrderOperationResult NotFound() => new(false, null, null);
    public static OrderOperationResult Ok(Order order) => new(true, order, null);
    public static OrderOperationResult Invalid(Order order, string error) => new(true, order, error);
}

public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public record OrderNotificationsView(bool Found, bool OwnedByCaller, IReadOnlyList<OrderNotification> Notifications)
{
    public static OrderNotificationsView NotFound() => new(false, false, new List<OrderNotification>());
    public static OrderNotificationsView NotOwned() => new(true, false, new List<OrderNotification>());
    public static OrderNotificationsView Owned(IReadOnlyList<OrderNotification> notifications) => new(true, true, notifications);
}
