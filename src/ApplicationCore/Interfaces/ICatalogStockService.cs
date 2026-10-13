using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Walmart;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of an order: how many of a catalog item to buy.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// eShop's own stock levels for catalog items, and order placement that draws them down. None of
/// this calls Walmart; Walmart learns the levels at the next sync.
/// </summary>
public interface ICatalogStockService
{
    /// <summary>Sets eShop's stock level for a catalog item. Throws if the item does not exist.</summary>
    Task SetStockAsync(int catalogItemId, int quantity, CancellationToken cancellationToken);

    /// <summary>eShop's stock level for a catalog item. An item never set has a level of 0.</summary>
    Task<int> GetStockAsync(int catalogItemId, CancellationToken cancellationToken);

    /// <summary>Every catalog item with its eShop level (0 where never set) and its Walmart SKU.</summary>
    Task<IReadOnlyList<CatalogItemStockLevel>> GetAllStockAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Places an order for the given lines, drawing the ordered quantities off eShop's stock.
    /// Refused (throws <see cref="Exceptions.InsufficientStockException"/>) if any line asks for
    /// more than is held. Reuses the existing <see cref="Order"/> aggregate. Returns the new order.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken);
}
