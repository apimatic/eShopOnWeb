using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All orders that have a payment record - used to line eShop orders up against
/// the payment provider's own transaction report.
/// </summary>
public class OrdersWithPaymentsSpecification : Specification<Order>
{
    public OrdersWithPaymentsSpecification()
    {
        Query.Where(o => o.Payment != null)
            .Include(o => o.Payment!)
            .ThenInclude(p => p.Refunds);
    }
}
