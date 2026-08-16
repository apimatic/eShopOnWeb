using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every payment that has a capture (money actually moved) — the eShop side of reconciliation.</summary>
public class CapturedPaymentsSpecification : Specification<Payment>
{
    public CapturedPaymentsSpecification()
    {
        Query
            .Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
