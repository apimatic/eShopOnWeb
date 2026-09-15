using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order with its items, but only when it belongs to the given buyer. Used to keep a shopper
/// from seeing or acting on another shopper's order.
/// </summary>
public sealed class OrderByIdForBuyerSpecification : Specification<Order>
{
    public OrderByIdForBuyerSpecification(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId)
             .Include(o => o.OrderItems)
                 .ThenInclude(i => i.ItemOrdered);
    }
}
