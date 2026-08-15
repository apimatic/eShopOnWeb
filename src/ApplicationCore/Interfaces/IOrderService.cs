using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities (used by the payments API,
    /// which carries the lines in the request rather than referencing a stored basket). Prices are
    /// taken from the catalog, and the returned order carries its generated id.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IEnumerable<OrderLine> lines, Address shippingAddress);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
