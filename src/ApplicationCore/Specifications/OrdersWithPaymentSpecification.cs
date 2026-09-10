using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every order with its payment state — an operator-scoped view used for reconciliation.</summary>
public sealed class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
