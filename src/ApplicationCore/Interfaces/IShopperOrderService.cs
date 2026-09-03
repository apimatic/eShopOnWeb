using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken);

    Task CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class CatalogOrderLine
{
    public CatalogOrderLine(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}

public sealed class ShopperOrderSummary
{
    public required Order Order { get; init; }
    public required IReadOnlyList<OrderNotification> Notifications { get; init; }
}

public sealed class NotificationReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationEntry> Matched { get; init; }
    public required IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; }
    public required IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; }
    public bool Truncated { get; init; }
}

public sealed class ReconciliationEntry
{
    public int? NotificationId { get; init; }
    public string? ProviderSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}
