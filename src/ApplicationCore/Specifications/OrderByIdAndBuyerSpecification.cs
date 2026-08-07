using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// An order by id, scoped to a single buyer so one shopper can never load another's order. Includes
/// order items so the total can be recomputed for payment.
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
