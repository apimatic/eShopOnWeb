using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every payment that has been captured (has a PayPal capture id), for reconciliation.</summary>
public class CapturedPaymentsSpecification : Specification<Payment>
{
    public CapturedPaymentsSpecification()
    {
        Query.Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
