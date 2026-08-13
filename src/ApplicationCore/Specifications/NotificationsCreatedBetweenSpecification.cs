using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application raised within a date range (by creation time), used to line eShop's
/// own record up against the provider's during reconciliation.
/// </summary>
public class NotificationsCreatedBetweenSpecification : Specification<Notification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedDate >= from && n.CreatedDate <= to)
            .OrderBy(n => n.CreatedDate);
    }
}
