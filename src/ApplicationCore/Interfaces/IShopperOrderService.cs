using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipTo, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public sealed record OrderLine(int CatalogItemId, int Quantity);

public sealed class ShopperOrderSummary
{
    public required Order Order { get; init; }
    public IReadOnlyList<OrderNotification> Notifications { get; init; } = Array.Empty<OrderNotification>();
}

public interface IOperatorOrderService
{
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
}

public interface IOrderNotificationQuery
{
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public interface INotificationOperatorService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public bool Truncated { get; init; }
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();
}

public sealed class ReconciliationEntry
{
    public int? NotificationId { get; init; }
    public string? ProviderSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public int? OrderId { get; init; }
    public string? DateSent { get; init; }
}
