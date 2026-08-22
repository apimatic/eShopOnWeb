using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IBuyerOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public OrderLineRequest(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}
