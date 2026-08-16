using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>An order by id with its items — used by operator actions that act on any order.</summary>
public class OrderByIdWithItemsSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdWithItemsSpecification(int orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
