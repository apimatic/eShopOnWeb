using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every order that has a payment (i.e. money has been held or moved), with its capture and refund
/// state, for operator-side reconciliation against PayPal's own records.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query.Where(o => o.Payment != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
