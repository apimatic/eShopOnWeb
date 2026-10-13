using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from catalog item ids and quantities (no basket), taking the ordered
/// quantities off eShop's own stock levels. Reuses the existing Order/OrderItem model.
/// </summary>
public interface IOrderPlacementService
{
    /// <summary>
    /// Creates an order for the given buyer. Throws
    /// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.InsufficientStockException"/> if any
    /// line asks for more than eShop holds; no stock is taken and no order is saved in that case.
    /// </summary>
    Task<int> PlaceOrderAsync(
        string buyerId,
        Address shipToAddress,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        CancellationToken cancellationToken = default);
}
