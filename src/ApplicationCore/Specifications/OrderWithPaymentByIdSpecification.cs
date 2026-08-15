using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order by id, eager-loading its items and refunds so a payment operation can act on the
/// full aggregate. Operator (admin) variant — not scoped to a buyer.
/// </summary>
public class OrderWithPaymentByIdSpecification : Specification<Order>
{
    public OrderWithPaymentByIdSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query.Include(o => o.Refunds);
    }
}
