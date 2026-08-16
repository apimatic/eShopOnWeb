using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments (with refunds) — used by the operator reconciliation report.</summary>
public class AllOrderPaymentsSpecification : Specification<OrderPayment>
{
    public AllOrderPaymentsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
