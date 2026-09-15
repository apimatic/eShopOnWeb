using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order with its items and full payment state (hold, capture, refunds)
/// so a later request can act on what PayPal owns.
/// </summary>
public class OrderWithPaymentByIdSpec : Specification<Order>, ISingleResultSpecification
{
    public OrderWithPaymentByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);

        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
