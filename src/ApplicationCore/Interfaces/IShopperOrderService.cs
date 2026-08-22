using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record ShopperOrderSummary(Order Order, IReadOnlyList<OrderNotification> Notifications);

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperOrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
