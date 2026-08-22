using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderFulfillmentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default);
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public interface INotificationOperatorService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ProviderOnlyMessage> ProviderOnly { get; init; } = Array.Empty<ProviderOnlyMessage>();
    public IReadOnlyList<LocalOnlyNotification> LocalOnly { get; init; } = Array.Empty<LocalOnlyNotification>();
}

public sealed class ReconciliationMatch
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string LocalStatus { get; init; } = string.Empty;
    public string ProviderStatus { get; init; } = string.Empty;
}

public sealed class ProviderOnlyMessage
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? Direction { get; init; }
    public string? DateSent { get; init; }
}

public sealed class LocalOnlyNotification
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string LocalStatus { get; init; } = string.Empty;
}
