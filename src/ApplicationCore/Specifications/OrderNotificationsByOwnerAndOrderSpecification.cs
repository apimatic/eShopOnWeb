using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOwnerAndOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerAndOrderSpecification(string ownerId, int orderId)
    {
        Query.Where(n => n.OwnerId == ownerId && n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}
