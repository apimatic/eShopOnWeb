using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderByIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdSpec(int orderId)
    {
        Query.Where(order => order.Id == orderId);
    }
}
