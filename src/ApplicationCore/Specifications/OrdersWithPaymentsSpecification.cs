using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every order that has any PayPal state — the local side of reconciliation.</summary>
public class OrdersWithPaymentsSpecification : Specification<Order>
{
    public OrdersWithPaymentsSpecification()
    {
        Query.Where(o => o.PayPalOrderId != null)
            .Include(o => o.Refunds);
    }
}
