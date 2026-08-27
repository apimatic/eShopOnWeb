using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsWithProviderSidInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderSid != null
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
