using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.CreatedAt >= fromUtc && n.CreatedAt <= toUtc
            && n.ProviderMessageSid != null);
    }
}
