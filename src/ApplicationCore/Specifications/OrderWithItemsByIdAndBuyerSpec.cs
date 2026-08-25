using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

// Scopes the lookup to the owning buyer so one shopper's order id can never be used to
// read or act on another shopper's order.
public class OrderWithItemsByIdAndBuyerSpec : Specification<Order>
{
    public OrderWithItemsByIdAndBuyerSpec(int orderId, string buyerId)
    {
        Query
            .Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
