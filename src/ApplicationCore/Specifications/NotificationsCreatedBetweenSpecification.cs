using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop recorded as accepted by the provider, created within the
/// given range. Used to line local records up against the provider's own list.
/// </summary>
public class NotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.MessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
