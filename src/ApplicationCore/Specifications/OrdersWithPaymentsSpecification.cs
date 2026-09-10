using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All orders that have a payment record, with the payment and its refunds loaded.
/// Used for reconciliation.</summary>
public class OrdersWithPaymentsSpecification : Specification<Order>
{
    public OrdersWithPaymentsSpecification()
    {
        Query
            .Where(o => o.Payment != null)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
