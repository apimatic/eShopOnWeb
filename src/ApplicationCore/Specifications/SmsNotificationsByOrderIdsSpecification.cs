using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SmsNotificationsByOrderIdsSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}
