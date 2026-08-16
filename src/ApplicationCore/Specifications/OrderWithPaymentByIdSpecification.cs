using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order with its items and its full payment graph (payment + refunds), so a
/// payment action can read and mutate the state PayPal owns.
/// </summary>
public class OrderWithPaymentByIdSpecification : Specification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);

        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
