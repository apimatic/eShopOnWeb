using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>An order by id, with its items and full payment state (hold, capture, refunds).</summary>
public sealed class OrderWithPaymentByIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
