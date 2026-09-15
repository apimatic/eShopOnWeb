using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments with their refunds — used by the operator reconciliation report.</summary>
public class PaymentsWithRefundsSpecification : Specification<Payment>
{
    public PaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
