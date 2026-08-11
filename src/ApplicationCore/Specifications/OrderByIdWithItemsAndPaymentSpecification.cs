using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order (with its items) for acting on its payment. The owned
/// <see cref="Order.Payment"/> loads with the order automatically. Optionally scopes to a buyer
/// so a shopper can only ever load their own order.
/// </summary>
public class OrderByIdWithItemsAndPaymentSpecification : Specification<Order>
{
    public OrderByIdWithItemsAndPaymentSpecification(int orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }

    public OrderByIdWithItemsAndPaymentSpecification(int orderId, string buyerId)
    {
        Query
            .Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
