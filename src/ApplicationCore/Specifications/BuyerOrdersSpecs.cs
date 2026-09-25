using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of one shopper's orders, most recent first, with their items.</summary>
public class BuyerOrdersSpec : Specification<Order>
{
    public BuyerOrdersSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
             .Include(o => o.OrderItems)
             .ThenInclude(i => i.ItemOrdered)
             .OrderByDescending(o => o.OrderDate);
    }
}

/// <summary>A single order scoped to its owner, so one shopper can never act on another's.</summary>
public class BuyerOrderByIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public BuyerOrderByIdSpec(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId)
             .Include(o => o.OrderItems)
             .ThenInclude(i => i.ItemOrdered);
    }
}
