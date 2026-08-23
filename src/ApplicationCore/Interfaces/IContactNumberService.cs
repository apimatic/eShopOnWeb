using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<BuyerContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<BuyerContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderCatalogItem> items, Address shipTo, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperOrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(string buyerId, int orderId, bool isAdmin, CancellationToken cancellationToken);
}

public interface IOrderFulfillmentService
{
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
}

public interface INotificationOperatorService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record OrderCatalogItem(int CatalogItemId, int Quantity);

public sealed record ShopperOrderSummary(
    Order Order,
    IReadOnlyList<OrderNotification> Notifications);

public sealed record ReconciliationMatch(string ProviderSid, int NotificationId);

public sealed record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int LocalNotificationCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<SmsMessageSnapshot> ProviderOnly,
    IReadOnlyList<OrderNotification> LocalOnly,
    bool Truncated);
