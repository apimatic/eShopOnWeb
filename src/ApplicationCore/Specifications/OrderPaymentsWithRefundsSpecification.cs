using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All order payments (with their refunds) — used by reconciliation.</summary>
public class OrderPaymentsWithRefundsSpecification : Specification<OrderPayment>
{
    public OrderPaymentsWithRefundsSpecification()
    {
        Query.Include(p => p.Refunds);
    }
}
