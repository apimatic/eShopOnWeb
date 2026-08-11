using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order with its items loaded (for totals) — the owned <see cref="Order.Payment"/>
/// and its refunds load automatically as part of the aggregate.
/// </summary>
public class OrderWithPaymentByIdSpecification : Specification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
