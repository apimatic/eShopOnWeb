using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id with its items eagerly loaded. The owned payment state and refunds are
/// loaded automatically as part of the aggregate.
/// </summary>
public class OrderWithItemsAndPaymentByIdSpec : Specification<Order>
{
    public OrderWithItemsAndPaymentByIdSpec(int orderId)
    {
        Query.Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
