using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    // Places an order directly from catalog item ids/quantities (no basket involved) - the path used
    // by PublicApi's POST /api/orders. Returns the created order, awaiting payment.
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress, IReadOnlyList<(int catalogItemId, int quantity)> items);
}
