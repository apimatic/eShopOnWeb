using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id, scoped to its owning buyer and including its items. Scoping to the buyer
/// ensures one shopper can never load, pay, or refund another shopper's order.
/// </summary>
public class OrderForBuyerWithItemsSpec : Specification<Order>
{
    public OrderForBuyerWithItemsSpec(string buyerId, int orderId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
