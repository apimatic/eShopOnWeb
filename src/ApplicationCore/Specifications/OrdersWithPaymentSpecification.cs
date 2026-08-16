using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All orders that have a payment, with capture/refund state — the eShop side of reconciliation.</summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query
            .Where(o => o.Payment != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
