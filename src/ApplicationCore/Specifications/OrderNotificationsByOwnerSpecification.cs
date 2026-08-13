using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId);
    }

    public OrderNotificationsByOwnerSpecification(string ownerId, IEnumerable<int> orderIds)
    {
        var ids = new HashSet<int>(orderIds);
        Query.Where(n => n.OwnerId == ownerId && ids.Contains(n.OrderId));
    }
}
