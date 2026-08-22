using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderCommand(
    string BuyerId,
    IReadOnlyList<PlaceOrderItem> Items,
    Address? ShipToAddress);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderMessage> ProviderOnly,
    IReadOnlyList<ReconciliationApplicationMessage> ApplicationOnly);

public record ReconciliationMatch(int NotificationId, string ProviderSid, string Status);
public record ReconciliationProviderMessage(string ProviderSid, string Status, DateTimeOffset? DateCreated, DateTimeOffset? DateSent);
public record ReconciliationApplicationMessage(int NotificationId, string? ProviderSid, string Status, string Kind);

public interface IShopOrderService
{
    Task<Order> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default);
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
