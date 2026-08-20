using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record CatalogOrderLine(int CatalogItemId, int Quantity);

public sealed record ReconciliationMismatch(
    string? NotificationId,
    string? ProviderSid,
    string Source,
    string? Status,
    string? DateSent);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int LocalCount,
    int MatchedCount,
    bool Truncated,
    IReadOnlyList<ReconciliationMismatch> Mismatches);

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipTo, CancellationToken cancellationToken);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
