using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order by id with its items and full payment state (authorization, capture, refunds),
/// so a payment operation can act on the PayPal state the order carries.
/// </summary>
public sealed class OrderWithPaymentByIdSpecification : Specification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query.Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds);
    }
}
