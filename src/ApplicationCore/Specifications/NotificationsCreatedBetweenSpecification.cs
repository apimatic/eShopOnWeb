using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications this app created within a window — the "what eShop believes it sent" side of reconciliation.</summary>
public class NotificationsCreatedBetweenSpecification : Specification<Notification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .OrderBy(n => n.Id);
    }
}
