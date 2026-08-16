using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Payments that have been captured (have a PayPal capture id), used for reconciliation.</summary>
public class CapturedPaymentsSpecification : Specification<OrderPayment>
{
    public CapturedPaymentsSpecification()
    {
        Query.Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
