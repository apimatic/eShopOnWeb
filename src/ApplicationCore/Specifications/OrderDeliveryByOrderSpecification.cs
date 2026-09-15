using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderDeliveryByOrderSpecification : Specification<OrderDelivery>
{
    public OrderDeliveryByOrderSpecification(int orderId)
    {
        Query.Where(d => d.OrderId == orderId);
    }
}
