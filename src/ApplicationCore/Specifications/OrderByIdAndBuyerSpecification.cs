using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order together with its items, scoped to the owning buyer so one shopper can
/// never act on another shopper's order.
/// </summary>
public class OrderByIdAndBuyerSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdAndBuyerSpecification(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
