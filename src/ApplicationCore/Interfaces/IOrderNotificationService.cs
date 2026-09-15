using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipTo, CancellationToken cancellationToken);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task<OrderNotification> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<NotificationReconciliationReport> ReconcileAsync(System.DateTimeOffset rangeStart, System.DateTimeOffset rangeEnd, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(IReadOnlyList<int> orderIds, CancellationToken cancellationToken);
}

public sealed record CatalogOrderLine(int CatalogItemId, int Quantity);

public sealed record NotificationReconciliationReport(
    System.DateTimeOffset From,
    System.DateTimeOffset To,
    string SendingNumber,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<ReconciliationRow> ProviderOnly,
    IReadOnlyList<ReconciliationRow> LocalOnly,
    bool Truncated);

public sealed record ReconciliationRow(
    string? ProviderSid,
    int? NotificationId,
    string? Status,
    string? DateSent,
    string? Kind);
