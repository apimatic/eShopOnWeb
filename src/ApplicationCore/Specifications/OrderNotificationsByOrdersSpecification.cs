using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification sent for any of a set of orders.</summary>
public class OrderNotificationsByOrdersSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrdersSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}
