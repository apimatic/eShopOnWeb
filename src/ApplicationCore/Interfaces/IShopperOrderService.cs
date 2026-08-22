using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogQuantity(int CatalogItemId, int Quantity);

public record ShopperOrderSummary(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderNotification> Notifications);

public record ReconciliationEntry(
    string? ProviderMessageSid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? LocalNotificationId,
    string Match);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IShopperOrderService
{
    Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogQuantity> items,
        Address? shipTo,
        CancellationToken cancellationToken = default);

    Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OrderNotification>>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default);

    Task<Result<OrderNotification>> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<Result<ReconciliationReport>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
