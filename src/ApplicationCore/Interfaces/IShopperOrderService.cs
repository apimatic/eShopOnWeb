using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public readonly record struct CatalogQuantity(int CatalogItemId, int Quantity);

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogQuantity> items, Address? shipToAddress, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);
}
