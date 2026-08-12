using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderStatusRecordByOrderIdSpecification : Specification<OrderStatusRecord>
{
    public OrderStatusRecordByOrderIdSpecification(int orderId)
    {
        Query.Where(o => o.OrderId == orderId);
    }
}
