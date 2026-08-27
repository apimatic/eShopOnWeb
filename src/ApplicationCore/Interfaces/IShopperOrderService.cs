using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record OrderNotificationView(
    int NotificationId,
    int OrderId,
    OrderNotificationKind Kind,
    string Status,
    string? ProviderMessageSid,
    string? Body,
    bool ContentDisposed,
    int? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledAt,
    int? ParentNotificationId);

public record ShopperOrderView(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<ShopperOrderItemView> Items,
    IReadOnlyList<OrderNotificationView> Notifications);

public record ShopperOrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record ResendNotificationResult(int NotificationId, bool AlreadyProcessed);

public record ReconciliationRow(
    string? ProviderMessageSid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    string Match);

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines);
    Task<Order> DispatchAsync(int orderId);
    Task<Order> CancelAsync(int orderId);
    Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId);
    Task<IReadOnlyList<OrderNotificationView>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator);
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey);
    Task DisposeContentAsync(int notificationId);
    Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
