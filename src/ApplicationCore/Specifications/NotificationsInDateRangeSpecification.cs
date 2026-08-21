using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsInDateRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
                (n.DateSent != null && n.DateSent >= from && n.DateSent <= to)
                || (n.DateSent == null && n.CreatedAt >= from && n.CreatedAt <= to))
            .OrderBy(n => n.Id);
    }
}
