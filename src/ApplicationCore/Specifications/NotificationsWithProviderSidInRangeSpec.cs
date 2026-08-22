using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsWithProviderSidInRangeSpec : Specification<OrderNotification>
{
    public NotificationsWithProviderSidInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedAt >= from
                         && n.CreatedAt <= to);
    }
}
