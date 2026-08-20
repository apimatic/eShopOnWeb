using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithItemsByIdSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithItemsByIdSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
