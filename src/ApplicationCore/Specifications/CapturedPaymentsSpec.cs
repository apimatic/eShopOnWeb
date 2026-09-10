using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Payments that have a capture (money actually taken), used to line eShop up against PayPal's
/// transaction record during reconciliation.
/// </summary>
public class CapturedPaymentsSpec : Specification<Payment>
{
    public CapturedPaymentsSpec()
    {
        Query
            .Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
