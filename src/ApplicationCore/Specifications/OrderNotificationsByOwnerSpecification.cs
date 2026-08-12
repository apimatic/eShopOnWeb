using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification belonging to a shopper across a set of their orders.</summary>
public class OrderNotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerSpecification(string ownerId, IEnumerable<int> orderIds)
    {
        var ids = new HashSet<int>(orderIds);
        Query.Where(n => n.OwnerId == ownerId && ids.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}
