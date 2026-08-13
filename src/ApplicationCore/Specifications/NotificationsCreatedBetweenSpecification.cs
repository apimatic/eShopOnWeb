using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications eShop recorded within a date range — the eShop side of reconciliation.</summary>
public class NotificationsCreatedBetweenSpecification : Specification<Notification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
