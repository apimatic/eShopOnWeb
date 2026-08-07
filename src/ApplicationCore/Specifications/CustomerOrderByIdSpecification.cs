using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id, scoped to the owning shopper so one shopper can never load another's order.
/// </summary>
public class CustomerOrderByIdSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public CustomerOrderByIdSpecification(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
