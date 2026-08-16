using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All orders whose payment has been captured (used for reconciliation).</summary>
public class CapturedOrdersSpecification : Specification<Order>
{
    public CapturedOrdersSpecification()
    {
        Query.Where(o => o.Payment != null && o.Payment.CaptureId != null);
    }
}
