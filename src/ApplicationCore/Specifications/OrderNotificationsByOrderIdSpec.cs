using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(notification => notification.OrderId == orderId)
            .OrderBy(notification => notification.CreatedAt);
    }
}
