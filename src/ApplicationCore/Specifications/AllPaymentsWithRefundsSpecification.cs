using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All payments with their refunds — the eShop side of the reconciliation report.</summary>
public class AllPaymentsWithRefundsSpecification : Specification<Payment>
{
    public AllPaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
