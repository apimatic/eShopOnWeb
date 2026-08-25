using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithRefundsSpecification : Specification<Order>
{
    public OrderWithRefundsSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
             .Include(o => o.OrderItems)
             .Include(o => o.Refunds);
    }
}
