using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record ShopperOrderSummary(
    Order Order,
    IReadOnlyList<OrderNotification> Notifications);

public record ReconciliationEntry(
    string? ProviderSid,
    int? NotificationId,
    string? LocalStatus,
    string? ProviderStatus,
    string? DateSent);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> LocalOnly,
    bool Truncated);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken);

    Task CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperOrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
