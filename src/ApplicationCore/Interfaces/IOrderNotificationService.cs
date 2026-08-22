using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record ReconciliationEntry(
    string? ProviderSid,
    string? Status,
    string? From,
    string? To,
    string? Body,
    string? DateSent,
    string? DateCreated,
    int? LocalNotificationId,
    bool InProvider,
    bool InApplication);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Entries,
    bool Complete);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> ListForOrdersAsync(
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
