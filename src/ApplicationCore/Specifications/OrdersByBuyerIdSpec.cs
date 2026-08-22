using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersByBuyerIdSpec : Specification<Order>
{
    public OrdersByBuyerIdSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate);
    }
}
