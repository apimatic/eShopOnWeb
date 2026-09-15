using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class CatalogOrderLine
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public interface ICatalogOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken = default);
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
