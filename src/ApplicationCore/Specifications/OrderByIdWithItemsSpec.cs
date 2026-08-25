using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderByIdWithItemsSpec : Specification<Order>
{
    public OrderByIdWithItemsSpec(int orderId)
    {
        Query.Where(o => o.Id == orderId)
             .Include(o => o.OrderItems);
    }
}
