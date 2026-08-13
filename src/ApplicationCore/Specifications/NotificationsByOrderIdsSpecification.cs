using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        var ids = new HashSet<int>(orderIds);
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.OrderId)
            .ThenBy(n => n.CreatedDate);
    }
}
