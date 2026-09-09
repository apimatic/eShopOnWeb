using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads every order with its payment state. Used by reconciliation to line eShop's own record of
/// payments up against PayPal's.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
