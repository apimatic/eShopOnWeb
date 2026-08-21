using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single order by id, with its items and full payment state (hold, capture, refunds).</summary>
public class OrderWithPaymentSpecification : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithPaymentSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
