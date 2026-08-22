using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationWorkflow
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken = default);
    Task<OrderLifecycleResult> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderLifecycleResult> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<OrderNotificationsResult> ListNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<NotificationContentResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class CatalogOrderLine
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PlaceOrderResult
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public Order? Order { get; init; }
}

public sealed class OrderLifecycleResult
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public Order? Order { get; init; }
}

public sealed class ShopperOrderSummary
{
    public required Order Order { get; init; }
    public required IReadOnlyList<OrderNotification> Notifications { get; init; }
}

public sealed class OrderNotificationsResult
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public Order? Order { get; init; }
    public IReadOnlyList<OrderNotification> Notifications { get; init; } = [];
}

public sealed class ResendNotificationResult
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public OrderNotification? Notification { get; init; }
}

public sealed class NotificationContentResult
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
}

public sealed class ReconciliationReport
{
    public bool Succeeded { get; init; }
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string? FromNumber { get; init; }
    public IReadOnlyList<ReconciliationItem> Matched { get; init; } = [];
    public IReadOnlyList<ReconciliationItem> ProviderOnly { get; init; } = [];
    public IReadOnlyList<ReconciliationItem> ApplicationOnly { get; init; } = [];
}

public sealed class ReconciliationItem
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? Status { get; init; }
    public string? Kind { get; init; }
    public int? OrderId { get; init; }
    public DateTimeOffset? ProviderDate { get; init; }
}
