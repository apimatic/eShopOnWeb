using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PlaceOrderItem(int CatalogItemId, int Quantity);

public sealed record PlaceOrderResult(Order Order);

public sealed record ShopperOrderSummary(Order Order, IReadOnlyList<OrderNotification> Notifications);

public interface IShopperOrderService
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>?> ListOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken);
}
