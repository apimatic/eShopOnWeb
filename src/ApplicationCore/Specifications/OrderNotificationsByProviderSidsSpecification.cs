using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpecification(IEnumerable<string> providerSids)
    {
        var sids = providerSids.ToArray();
        Query.Where(n => n.ProviderMessageSid != null && sids.Contains(n.ProviderMessageSid));
    }
}
