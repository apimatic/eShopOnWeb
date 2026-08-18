using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider (they have a message SID) within a date range,
/// used to line eShop's record up against the provider's for reconciliation.
/// </summary>
public class SentNotificationsInRangeSpecification : Specification<Notification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedAt >= from &&
            n.CreatedAt <= to);
    }
}
