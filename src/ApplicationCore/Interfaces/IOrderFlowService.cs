using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderRequest(IReadOnlyList<PlaceOrderItem> Items, Address? ShipToAddress);

public record ShopperOrdersResult(IReadOnlyList<Order> Orders, IReadOnlyList<OrderNotification> Notifications);

public interface IOrderFlowService
{
    Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken = default);
    Task<ShopperOrdersResult> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrderAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
}
