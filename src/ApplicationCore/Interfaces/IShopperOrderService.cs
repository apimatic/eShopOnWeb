using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public record ReconciliationMatch(
    string? ProviderSid,
    int? NotificationId,
    string? ProviderStatus,
    string? LocalStatus,
    string Alignment);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matches,
    int ProviderCount,
    int LocalCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int LocalOnlyCount);

public interface IShopperOrderService
{
    Task<OrderWithNotifications> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderWithNotifications>> ListMineAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken);
    Task<OrderWithNotifications> DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderWithNotifications> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
