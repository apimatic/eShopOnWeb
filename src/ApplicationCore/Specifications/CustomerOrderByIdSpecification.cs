using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single order by id, scoped to its owning shopper, with its items loaded.</summary>
public class CustomerOrderByIdSpecification : Specification<Order>
{
    public CustomerOrderByIdSpecification(string buyerId, int orderId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
