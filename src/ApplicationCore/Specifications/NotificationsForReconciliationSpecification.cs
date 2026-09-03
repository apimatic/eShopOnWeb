using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsForReconciliationSpecification : Specification<OrderNotification>
{
    public NotificationsForReconciliationSpecification(DateTimeOffset from, DateTimeOffset to, IEnumerable<string> providerSids)
    {
        var sids = providerSids.ToList();
        Query.Where(n =>
            (n.CreatedAt >= from && n.CreatedAt <= to)
            || (n.ProviderSid != null && sids.Contains(n.ProviderSid)));
    }
}
