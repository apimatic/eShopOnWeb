using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>An order by id, scoped to its owner so one shopper can never act on another's.</summary>
public class OrderByIdForBuyerSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdForBuyerSpecification(int orderId, string buyerId)
    {
        Query
            .Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
