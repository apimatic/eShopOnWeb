using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments for which money has moved at PayPal (an order was created), so they can be lined up
/// against PayPal's own transaction record during reconciliation.
/// </summary>
public class ReconcilablePaymentsSpecification : Specification<Payment>
{
    public ReconcilablePaymentsSpecification()
    {
        Query.Where(p => p.PayPalOrderId != null)
             .Include(p => p.Refunds);
    }
}
