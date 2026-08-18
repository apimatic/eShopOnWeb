using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it sent immediately from its configured sending number within a
/// date range — i.e. those that carry a provider Sid and are not the scheduled follow-up (which the
/// provider sends from its messaging-service pool, not the configured From number). These are what a
/// reconciliation lines up against the provider's own record for that number.
/// </summary>
public class NotificationsSentInRangeSpecification : Specification<Notification>
{
    public NotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && !n.IsScheduled
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
