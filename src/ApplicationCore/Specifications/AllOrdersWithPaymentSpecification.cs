using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All orders with their payment state — used by the operator reconciliation report.</summary>
public sealed class AllOrdersWithPaymentSpecification : Specification<Order>
{
    public AllOrdersWithPaymentSpecification()
    {
        Query.Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds);
    }
}
