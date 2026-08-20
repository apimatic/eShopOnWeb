using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderItem(int CatalogItemId, int Quantity);

public record ReconciliationItem(
    int? NotificationId,
    string? ProviderMessageSid,
    string? ProviderStatus,
    string Kind,
    string? Body);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationItem> Matched,
    IReadOnlyList<ReconciliationItem> ProviderOnly,
    IReadOnlyList<ReconciliationItem> ApplicationOnly);

public interface IOrderLifecycleService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderItem> items, Address? shipToAddress, CancellationToken cancellationToken = default);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string callerId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
