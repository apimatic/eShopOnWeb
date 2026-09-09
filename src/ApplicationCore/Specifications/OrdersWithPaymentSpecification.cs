using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every order that has an associated payment, with its payment state — used to line eShop
/// records up against PayPal's own transaction record during reconciliation.</summary>
public sealed class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query.Where(o => o.Payment != null);
        Query.Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds);
    }
}
