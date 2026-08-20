using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record CatalogLine(int CatalogItemId, int Quantity);

public sealed record ReconciliationRow(
    string? NotificationId,
    string? ProviderMessageSid,
    string? ApplicationStatus,
    string? ProviderStatus,
    string Match);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<ReconciliationRow> ProviderOnly,
    IReadOnlyList<ReconciliationRow> ApplicationOnly);

public interface IOrderSmsService
{
    Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OrderNotification>>> ListOrderNotificationsAsync(
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

    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken = default);
}
