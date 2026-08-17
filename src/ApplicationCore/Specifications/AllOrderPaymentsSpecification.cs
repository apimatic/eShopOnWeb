using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments with their refunds — used by operator reconciliation across every shopper.</summary>
public class AllOrderPaymentsSpecification : Specification<OrderPayment>
{
    public AllOrderPaymentsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
