using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notifications eShop believes it handed to the provider within a date range — those with a provider
/// message identifier and created within [from, to]. Used as the eShop side of a reconciliation.
/// </summary>
public class NotificationsWithProviderSidBetweenSpecification : Specification<Notification>
{
    public NotificationsWithProviderSidBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedDate >= from &&
            n.CreatedDate <= to);
    }
}
