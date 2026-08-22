using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        CancellationToken cancellationToken = default);

    Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OrderNotification>>> ListNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    Task<Result<OrderNotification>> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<Result<NotificationReconciliationReport>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task RefreshProviderStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<NotificationReconciliationEntry> Entries { get; init; } = Array.Empty<NotificationReconciliationEntry>();
    public int ProviderCount { get; init; }
    public int EshopCount { get; init; }
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EshopOnlyCount { get; init; }
}

public sealed class NotificationReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public int? NotificationId { get; init; }
    public string? Kind { get; init; }
    public string? ProviderStatus { get; init; }
    public string? DateSent { get; init; }
    public string? Direction { get; init; }
    public bool InProvider { get; init; }
    public bool InEshop { get; init; }
}
