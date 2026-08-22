using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogItemQuantity> items, CancellationToken cancellationToken = default);
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order?> GetForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken = default);
}

public sealed class CatalogItemQuantity
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}
