using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All orders that have a payment attached (i.e. have moved past awaiting-payment). Used by
/// reconciliation to line eShop's side up against PayPal's transaction records. The owned payment is
/// auto-included; filtering to those with a payment is done in memory by the caller.
/// </summary>
public class OrdersWithItemsSpecification : Specification<Order>
{
    public OrdersWithItemsSpecification()
    {
        Query
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
