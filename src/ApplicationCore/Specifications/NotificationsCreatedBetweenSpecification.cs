using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications eShop created within a date range (for reconciliation against the provider).</summary>
public class NotificationsCreatedBetweenSpecification : Specification<Notification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedDate >= from && n.CreatedDate <= to)
            .OrderBy(n => n.CreatedDate);
    }
}
