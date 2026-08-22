using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsCreatedInRangeSpec : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedUtc >= from && n.CreatedUtc <= to);
    }
}
