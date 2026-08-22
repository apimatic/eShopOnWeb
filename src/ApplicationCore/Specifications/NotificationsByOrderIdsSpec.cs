using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToList();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}
