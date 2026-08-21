using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
                (n.CreatedAt >= from && n.CreatedAt <= to) ||
                (n.ProviderDateSent != null && n.ProviderDateSent >= from && n.ProviderDateSent <= to) ||
                (n.ScheduledSendAt != null && n.ScheduledSendAt >= from && n.ScheduledSendAt <= to))
            .OrderBy(n => n.CreatedAt);
    }
}
