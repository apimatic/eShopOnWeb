using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider (they have a SID) within a date range —
/// the eShop side of a reconciliation.
/// </summary>
public class NotificationsSentInRangeSpecification : Specification<Notification>
{
    public NotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.CreatedDate >= from
            && n.CreatedDate <= to);
    }
}
