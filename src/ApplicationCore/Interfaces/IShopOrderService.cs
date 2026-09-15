using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogQuantity(int CatalogItemId, int Quantity);

public record ShopperOrderView(Order Order, IReadOnlyList<OrderNotification> Notifications);

public interface IShopOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogQuantity> items, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(string buyerId, int orderId, bool isAdministrator, CancellationToken cancellationToken);
}
