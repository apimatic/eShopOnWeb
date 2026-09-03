using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record ShopOrderDetail(
    Order Order,
    IReadOnlyList<OrderNotification> Notifications);

public record ReconciliationRow(
    string? NotificationId,
    string? ProviderSid,
    string? Status,
    string? Kind,
    string Bucket);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Truncated,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<ReconciliationRow> ProviderOnly,
    IReadOnlyList<ReconciliationRow> EshopOnly);

public interface IShopOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipTo, CancellationToken ct);
    Task<Order> DispatchAsync(int orderId, CancellationToken ct);
    Task<Order> CancelAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<ShopOrderDetail>> GetMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);
}

public interface INotificationAdminService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task RedactContentAsync(int notificationId, CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
