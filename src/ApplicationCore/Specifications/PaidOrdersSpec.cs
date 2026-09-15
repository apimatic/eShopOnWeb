using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads every order that has a payment (any hold, capture or refund), with the payment state,
/// so its PayPal ids can be lined up against PayPal's transaction report during reconciliation.
/// </summary>
public class PaidOrdersSpec : Specification<Order>
{
    public PaidOrdersSpec()
    {
        Query.Where(o => o.Payment != null);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
