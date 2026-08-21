using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matches,
    IReadOnlyList<ProviderMessage> ProviderOnly,
    IReadOnlyList<OrderNotification> ApplicationOnly);

public record ReconciliationMatch(OrderNotification Notification, ProviderMessage ProviderMessage);

public interface IOrderWorkflowService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        int orderId,
        string buyerId,
        CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
