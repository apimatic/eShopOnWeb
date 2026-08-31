using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All payments with their refunds. Used by reconciliation to line up every
/// PayPal-owned id (authorization, capture, refund) against PayPal's own records.
/// </summary>
public class PaymentsWithRefundsSpec : Specification<Payment>
{
    public PaymentsWithRefundsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
