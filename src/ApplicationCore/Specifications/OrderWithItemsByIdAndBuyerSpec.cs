using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order (with its items) that belongs to a given buyer. Scoping by
/// <paramref name="buyerId"/> ensures one shopper can never load, pay or refund another's order.
/// </summary>
public class OrderWithItemsByIdAndBuyerSpec : Specification<Order>
{
    public OrderWithItemsByIdAndBuyerSpec(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
