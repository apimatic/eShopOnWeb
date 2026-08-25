using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Looks up a single order, scoped to the buyer that owns it, so a shopper can never act on another's order.</summary>
public class OrderByIdForBuyerSpec : Specification<Order>
{
    public OrderByIdForBuyerSpec(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
