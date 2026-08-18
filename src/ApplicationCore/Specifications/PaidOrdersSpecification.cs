using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All orders that carry a payment, with capture and refund state — backs reconciliation.</summary>
public class PaidOrdersSpecification : Specification<Order>
{
    public PaidOrdersSpecification()
    {
        Query
            .Where(o => o.Payment != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
