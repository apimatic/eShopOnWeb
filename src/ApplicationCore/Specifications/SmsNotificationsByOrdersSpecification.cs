using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications belonging to any of the given orders (used to summarise my-orders).</summary>
public sealed class SmsNotificationsByOrdersSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrdersSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
             .OrderBy(n => n.Id);
    }
}
