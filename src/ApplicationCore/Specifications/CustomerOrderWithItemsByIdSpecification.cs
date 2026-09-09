using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// An order by id, scoped to its owner and with its items loaded so the total can be computed. Used
/// for shopper-scoped actions that must not leak or act on another shopper's order.
/// </summary>
public class CustomerOrderWithItemsByIdSpecification : Specification<Order>
{
    public CustomerOrderWithItemsByIdSpecification(int orderId, string buyerId)
    {
        Query
            .Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
