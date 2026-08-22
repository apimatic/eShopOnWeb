using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record CatalogOrderLine(int CatalogItemId, int Quantity);

public sealed record ShopperOrderSummary(Order Order, IReadOnlyList<OrderNotification> Notifications);

public interface IShopperOrderService
{
    Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<OrderNotification>>> ListNotificationsAsync(string buyerId, int orderId, bool isOperator, CancellationToken cancellationToken);
}

public interface IOperatorNotificationService
{
    Task<Result<Order>> DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<Result<ReconciliationReport>> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ReconciliationReport(
    System.DateTimeOffset From,
    System.DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<SmsMessageSnapshot> ProviderMessages,
    IReadOnlyList<OrderNotification> ApplicationMessages,
    IReadOnlyList<string> OnlyInProvider,
    IReadOnlyList<string> OnlyInApplication,
    IReadOnlyList<string> Matched);
