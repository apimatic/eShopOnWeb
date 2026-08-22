using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record OrderLine(int CatalogItemId, int Quantity);

public sealed record ReconciliationEntry(
    string? ProviderSid,
    int? NotificationId,
    string? ProviderStatus,
    string? EshopStatus,
    string Match);

public sealed record ShopperOrdersResult(
    IReadOnlyList<Order> Orders,
    IReadOnlyList<OrderNotification> Notifications);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<ShopperOrdersResult> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
