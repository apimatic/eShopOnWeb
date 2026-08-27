using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.CreatedAt >= fromUtc && n.CreatedAt <= toUtc)
            .OrderBy(n => n.CreatedAt);
    }
}
