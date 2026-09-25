using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// An order by its id, with items. The owned <see cref="Order.Payment"/> (and its refunds) is loaded
/// automatically by EF as an owned navigation.
/// </summary>
public class OrderWithPaymentByIdSpec : Specification<Order>
{
    public OrderWithPaymentByIdSpec(int orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
