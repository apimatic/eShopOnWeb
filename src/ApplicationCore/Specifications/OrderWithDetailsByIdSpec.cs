using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>An order with everything the payment lifecycle needs: items and refunds.</summary>
public class OrderWithDetailsByIdSpec : Specification<Order>
{
    public OrderWithDetailsByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds);
    }
}
