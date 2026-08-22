using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);

public sealed record OrderNotificationView(
    int NotificationId,
    int OrderId,
    OrderNotificationKind Kind,
    string? Body,
    bool ContentDisposed,
    string? ProviderSid,
    string? ProviderStatus,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ScheduledForUtc,
    int? ResendOfNotificationId);

public sealed record ShopperOrderView(
    int OrderId,
    string Status,
    decimal Total,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderNotificationView> Notifications);

public sealed record ReconciliationRow(
    string Sid,
    string Match,
    string? EShopStatus,
    string? ProviderStatus,
    int? NotificationId,
    string? DateSent);

public interface IOrderMessagingService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address shipTo, CancellationToken cancellationToken);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotificationView>> ListNotificationsAsync(int orderId, string callerBuyerId, bool isAdministrator, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}
