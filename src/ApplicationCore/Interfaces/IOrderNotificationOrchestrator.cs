using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderAddress(string Street, string City, string State, string Country, string ZipCode);

public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    string Match,
    string? ProviderStatus,
    string? EshopStatus,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? EshopCreatedAt);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Entries,
    int MatchedCount,
    int ProviderOnlyCount,
    int EshopOnlyCount);

public interface IOrderNotificationOrchestrator
{
    Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        PlaceOrderAddress? address,
        CancellationToken cancellationToken = default);

    Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OrderNotification>>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
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

    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default);
}
