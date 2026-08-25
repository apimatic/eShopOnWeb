using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads every order with its payment and refunds so reconciliation can cross-reference
/// PayPal's transaction report against what eShop knows about, regardless of when the order's
/// payment activity happened. Fine at the scale of a reference app's in-memory/local database.</summary>
public sealed class AllOrdersWithPaymentSpecification : Specification<Order>
{
    public AllOrdersWithPaymentSpecification()
    {
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
