using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopOrderService
{
    Task<(Order Order, OrderNotification? Notification)> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItem> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default);

    Task<(Order Order, IReadOnlyList<OrderNotification> Notifications)> DispatchAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<(Order Order, IReadOnlyList<OrderNotification> Notifications)> CancelAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);
}

public sealed class CatalogOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}
