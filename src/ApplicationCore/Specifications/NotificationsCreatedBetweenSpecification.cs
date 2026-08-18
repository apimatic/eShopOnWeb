using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages it believes it sent within a date range: notifications that
/// reached the provider (carry a SID) and were created in [from, to]. The reconciliation report
/// lines these up against the provider's own list for the same range.
/// </summary>
public sealed class NotificationsCreatedBetweenSpecification : Specification<Notification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null
            && n.CreatedDate >= from
            && n.CreatedDate <= to);
    }
}
