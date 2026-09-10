using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every order that has a payment, with its full payment graph. Used by reconciliation to line up
/// eShop's own record against PayPal's transaction report.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query
            .Where(o => o.Payment != null)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
