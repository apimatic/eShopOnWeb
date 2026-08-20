using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsCreatedBetweenSpec : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedUtc >= from && n.CreatedUtc <= to);
    }
}
