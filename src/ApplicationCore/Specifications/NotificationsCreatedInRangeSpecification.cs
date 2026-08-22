using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to && n.ProviderMessageSid != null);
    }
}
