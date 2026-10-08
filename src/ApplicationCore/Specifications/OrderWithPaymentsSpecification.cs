using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentsSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentsSpecification(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payments)
            .Include(o => o.Refunds);
    }
}
