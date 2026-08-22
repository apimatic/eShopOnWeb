using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsWithProviderSidInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.ProviderSid != null && n.CreatedUtc >= fromUtc && n.CreatedUtc <= toUtc);
    }
}
