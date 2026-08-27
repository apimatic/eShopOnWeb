using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderByOwnerAndIdWithItemsSpec : Specification<Order>
{
    public OrderByOwnerAndIdWithItemsSpec(string buyerId, int orderId)
    {
        Query.Where(order => order.Id == orderId && order.BuyerId == buyerId)
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered);
    }
}
