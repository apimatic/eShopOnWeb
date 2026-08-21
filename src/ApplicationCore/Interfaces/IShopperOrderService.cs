using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public class PlaceOrderRequest
{
    public required string BuyerId { get; init; }
    public required IReadOnlyList<PlaceOrderItem> Items { get; init; }
    public required Address ShipTo { get; init; }
}

public class ShopperOrderSummary
{
    public required Order Order { get; init; }
    public required IReadOnlyList<OrderNotification> Notifications { get; init; }
}

public class NotificationReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    public required IReadOnlyList<TwilioMessageSnapshot> ProviderOnly { get; init; }
    public required IReadOnlyList<OrderNotification> EshopOnly { get; init; }
}

public class ReconciliationMatch
{
    public required OrderNotification Notification { get; init; }
    public required TwilioMessageSnapshot ProviderMessage { get; init; }
}

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
}

public interface IOperatorOrderService
{
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
