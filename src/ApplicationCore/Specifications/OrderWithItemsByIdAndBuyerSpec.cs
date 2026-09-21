using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single order (with its items) belonging to a specific buyer. Scoping by buyer here means one
/// shopper can never see or act on another's order. The owned <see cref="Order.Payment"/> and its refunds
/// are loaded automatically by EF as part of the aggregate.
/// </summary>
public class OrderWithItemsByIdAndBuyerSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderWithItemsByIdAndBuyerSpec(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
