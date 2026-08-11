using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order with its items and refunds so a payment operation can act on the full aggregate.
/// </summary>
public class OrderByIdWithPaymentSpec : Specification<Order>
{
    public OrderByIdWithPaymentSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query.Include(o => o.Refunds);
    }
}
