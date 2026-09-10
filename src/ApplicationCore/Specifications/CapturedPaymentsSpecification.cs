using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments that reached a capture — the eShop side of reconciliation.</summary>
public class CapturedPaymentsSpecification : Specification<Payment>
{
    public CapturedPaymentsSpecification()
    {
        Query.Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
