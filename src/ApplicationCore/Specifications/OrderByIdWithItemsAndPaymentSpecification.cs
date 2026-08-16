using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order with its items. The owned <see cref="Order.Payment"/> (and its refunds) is
/// included automatically by EF Core as part of the aggregate.
/// </summary>
public class OrderByIdWithItemsAndPaymentSpecification : Specification<Order>
{
    public OrderByIdWithItemsAndPaymentSpecification(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
