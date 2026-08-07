using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single order, scoped to the shopper who placed it (with its items). Filtering by buyer keeps
/// one shopper from paying for or refunding another shopper's order: a mismatch yields nothing.
/// </summary>
public class CustomerOrderByIdSpecification : Specification<Order>
{
    public CustomerOrderByIdSpecification(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
