using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order (with its items) that belongs to a specific buyer. Scoping the query by buyer id
/// means one shopper can never load, pay or refund another shopper's order.
/// </summary>
public class OrderByIdWithItemsForBuyerSpecification : Specification<Order>
{
    public OrderByIdWithItemsForBuyerSpecification(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
