using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogQuantity> items, Address shippingAddress, CancellationToken cancellationToken);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken);

    Task CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderWithNotifications>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> GetNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class OrderWithNotifications
{
    public required Order Order { get; init; }
    public required IReadOnlyList<OrderNotification> Notifications { get; init; }
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public bool Truncated { get; init; }
    public required IReadOnlyList<ReconciliationRow> Matched { get; init; }
    public required IReadOnlyList<ReconciliationRow> ProviderOnly { get; init; }
    public required IReadOnlyList<ReconciliationRow> EShopOnly { get; init; }
}

public sealed class ReconciliationRow
{
    public int? NotificationId { get; init; }
    public string? ProviderSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public int? OrderId { get; init; }
}
