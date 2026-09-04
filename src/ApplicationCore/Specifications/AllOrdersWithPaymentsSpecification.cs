using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every order with items and the full payment record (including refunds).</summary>
public class AllOrdersWithPaymentsSpecification : Specification<Order>
{
    public AllOrdersWithPaymentsSpecification()
    {
        Query.Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
             .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
