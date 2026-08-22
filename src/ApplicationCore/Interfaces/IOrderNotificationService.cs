using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLine(int CatalogItemId, int Quantity);

public record ShipToAddress(string Street, string City, string State, string Country, string ZipCode);

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, ShipToAddress? shipTo, CancellationToken ct);

    Task DispatchAsync(int orderId, CancellationToken ct);

    Task CancelAsync(int orderId, CancellationToken ct);

    Task<BuyerOrdersResult> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(int orderId, string? buyerId, CancellationToken ct);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    Task RedactContentAsync(int notificationId, CancellationToken ct);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record BuyerOrdersResult(
    IReadOnlyList<Order> Orders,
    IReadOnlyList<OrderNotification> Notifications);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> LocalOnly,
    bool Truncated);

public sealed record ReconciliationEntry(
    int? NotificationId,
    string? ProviderSid,
    string? Status,
    string? DateSent,
    string? Kind);
