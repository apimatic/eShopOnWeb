using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CustomerOrderByIdSpecification : Specification<Order>
{
    public CustomerOrderByIdSpecification(int orderId, string buyerId)
    {
        Query.Where(o => o.Id == orderId && o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered);
    }
}
