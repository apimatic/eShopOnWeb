using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line of a directly-placed order: a catalog item and the quantity ordered.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog items (rather than from a basket), reusing the same
    /// Order/OrderItem model. Unit prices are taken from the catalog. Returns the persisted order,
    /// which starts awaiting payment.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shippingAddress);
}
