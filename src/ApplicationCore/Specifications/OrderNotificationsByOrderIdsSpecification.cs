using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        Query.AsNoTracking()
            .Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}
