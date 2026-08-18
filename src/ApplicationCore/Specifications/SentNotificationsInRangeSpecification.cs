using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notifications eShop believes it sent from its own sending number within a date-time range — i.e. those
/// that reached the provider (have a SID) and were immediate sends (not scheduled follow-ups, which go out via
/// the messaging service under a different number). This is the eShop side that reconciliation lines up against
/// the provider's own record.
/// </summary>
public class SentNotificationsInRangeSpecification : Specification<Notification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderSid != null
            && !n.IsScheduled
            && n.CreatedDate >= from
            && n.CreatedDate <= to);
    }
}
