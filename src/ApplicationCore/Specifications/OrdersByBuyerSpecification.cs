using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's orders, most recent first, with their items.</summary>
public class OrdersByBuyerSpecification : Specification<Order>
{
    public OrdersByBuyerSpecification(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate);
    }
}
