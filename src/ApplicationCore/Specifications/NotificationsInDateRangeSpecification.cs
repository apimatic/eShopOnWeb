using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsInDateRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .OrderBy(n => n.CreatedAt);
    }
}
