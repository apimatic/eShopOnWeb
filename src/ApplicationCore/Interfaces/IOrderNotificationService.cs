using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class CatalogOrderLine
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public bool Complete { get; init; }
    public IReadOnlyList<ReconciliationEntry> Entries { get; init; } = Array.Empty<ReconciliationEntry>();
}

public sealed class ReconciliationEntry
{
    public string Match { get; init; } = string.Empty;
    public string? ProviderSid { get; init; }
    public int? NotificationId { get; init; }
    public string? Status { get; init; }
    public string? DateSent { get; init; }
}

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipTo, CancellationToken cancellationToken = default);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
