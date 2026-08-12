using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for a set of orders (used to show my-orders with their notification state).</summary>
public class OrderNotificationsForOrdersSpecification : Specification<OrderNotification>
{
    public OrderNotificationsForOrdersSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
             .OrderBy(n => n.CreatedAt);
    }
}
