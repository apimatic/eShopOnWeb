using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments that have been captured — the eShop side of reconciliation (across all shoppers).</summary>
public class CapturedOrderPaymentsSpecification : Specification<OrderPayment>
{
    public CapturedOrderPaymentsSpecification()
    {
        Query.Where(p => p.CaptureId != null)
            .Include(p => p.Refunds);
    }
}
