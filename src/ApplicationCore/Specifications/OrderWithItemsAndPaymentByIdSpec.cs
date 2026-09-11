using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithItemsAndPaymentByIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithItemsAndPaymentByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
