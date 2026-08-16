using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order with its items. The owned <see cref="OrderPayment"/> (and its refunds)
/// are automatically included by EF, so this is enough to act on the order's payment.
/// </summary>
public class OrderWithItemsAndPaymentByIdSpec : Specification<Order>
{
    public OrderWithItemsAndPaymentByIdSpec(int orderId)
    {
        Query
            .Where(order => order.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
