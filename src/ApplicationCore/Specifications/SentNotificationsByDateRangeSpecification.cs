using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider within a date range — those with a
/// provider SID and a send time inside [from, to]. This is the "eShop side" of reconciliation.
/// </summary>
public class SentNotificationsByDateRangeSpecification : Specification<Notification>
{
    public SentNotificationsByDateRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderSid != null &&
            n.SentAt != null &&
            n.SentAt >= from &&
            n.SentAt <= to);
    }
}
