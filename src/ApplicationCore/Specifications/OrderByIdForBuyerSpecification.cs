using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id, scoped to the buyer that placed it and including its items. Scoping every
/// order read to the caller keeps one shopper from ever seeing or acting on another's order.
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
