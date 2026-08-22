using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record CatalogOrderLine(int CatalogItemId, int Quantity);

public sealed record NotificationView(
    int NotificationId,
    NotificationKind Kind,
    string? Body,
    string? ProviderSid,
    string? ProviderStatus,
    int? ErrorCode,
    string? ErrorMessage,
    bool ContentRedacted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    int? ResentFromNotificationId,
    string? SendFailure);

public sealed record ShopperOrderView(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<ShopperOrderItemView> Items,
    IReadOnlyList<NotificationView> Notifications);

public sealed record ShopperOrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public sealed record ReconciliationMessageRow(
    string? ProviderSid,
    string? ProviderStatus,
    string? DateSent,
    int? NotificationId,
    string Alignment);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EshopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EshopOnlyCount,
    bool Truncated,
    IReadOnlyList<ReconciliationMessageRow> Messages);

public interface IShopperOrderService
{
    Task<Order> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationView>> ListNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
