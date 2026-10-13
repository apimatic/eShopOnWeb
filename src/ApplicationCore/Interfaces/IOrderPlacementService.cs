using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from catalog item ids and quantities (reusing the Order aggregate),
/// deducting the ordered quantities from eShop's own stock levels.
/// </summary>
public interface IOrderPlacementService
{
    /// <summary>
    /// Creates an order for the given buyer and lines, removing each ordered quantity from the
    /// matching catalog item's eShop stock. Throws if any line exceeds available stock or names
    /// an unknown catalog item. Returns the new order's id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: how many units of a catalog item to buy.</summary>
public readonly record struct OrderLine(int CatalogItemId, int Quantity);
