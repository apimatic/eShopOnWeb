using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Orders that have any PayPal payment state, for lining up against the provider's
/// own transaction records during reconciliation.
/// </summary>
public class PaidOrdersSpecification : Specification<Order>
{
    public PaidOrdersSpecification()
    {
        Query.Where(o => o.PayPalOrderId != null)
            .Include(o => o.Refunds);
    }
}
