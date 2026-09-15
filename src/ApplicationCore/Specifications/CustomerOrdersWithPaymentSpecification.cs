using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A buyer's orders together with their payment state, for the my-orders view.
/// </summary>
public class CustomerOrdersWithPaymentSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentSpecification(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);

        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
