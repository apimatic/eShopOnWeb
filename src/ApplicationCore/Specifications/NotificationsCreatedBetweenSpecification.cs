using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own notifications created within a date range, used to line up against the
/// provider's record during reconciliation.
/// </summary>
public class NotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.CreatedAt >= fromUtc && n.CreatedAt <= toUtc);
    }
}
