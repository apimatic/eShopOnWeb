using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record OrderPlacementResult(Order Order, OrderStatus Status, IReadOnlyList<OrderNotification> Notifications);

public record ShopperOrder(Order Order, OrderStatus Status);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<TwilioMessageResult> ProviderOnly,
    IReadOnlyList<OrderNotification> ApplicationOnly);

public record ReconciliationMatch(OrderNotification Notification, TwilioMessageResult ProviderMessage);

public interface IShopperOrderService
{
    Task<OrderPlacementResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperOrder>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ShopperOrder?> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);

    Task<ShopperOrder> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<ShopperOrder> CancelAsync(int orderId, CancellationToken cancellationToken = default);
}

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
