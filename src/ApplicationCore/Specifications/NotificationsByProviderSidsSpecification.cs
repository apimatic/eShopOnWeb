using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsInCreatedRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInCreatedRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class NotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpecification(IEnumerable<string> providerSids)
    {
        Query.Where(n => n.ProviderMessageSid != null && providerSids.Contains(n.ProviderMessageSid));
    }
}
