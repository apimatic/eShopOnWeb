using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderByIdSpecification : Specification<Order>, ISingleResultSpecification
{
    public OrderByIdSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId);
    }
}
