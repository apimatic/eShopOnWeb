using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single order with its items, scoped to its buyer so a shopper only ever sees their own.</summary>
public class OrderByIdAndBuyerSpecification : Specification<Order>
{
    public OrderByIdAndBuyerSpecification(int orderId, string buyerId)
    {
        Query
            .Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
