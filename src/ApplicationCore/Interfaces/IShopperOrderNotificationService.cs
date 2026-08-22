using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record NotificationReconciliationItem(
    string? ProviderSid,
    string Status,
    string? Body,
    string? DateCreated,
    string? DateSent,
    int? LocalNotificationId,
    string Source);

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<NotificationReconciliationItem> Matched,
    IReadOnlyList<NotificationReconciliationItem> ProviderOnly,
    IReadOnlyList<NotificationReconciliationItem> LocalOnly,
    bool Truncated);

public interface IShopperOrderNotificationService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> items,
        Address? shipTo,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListNotificationsForShopperOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken);

    Task CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
