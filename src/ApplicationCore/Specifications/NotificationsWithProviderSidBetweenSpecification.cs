using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it actually sent (they carry a provider message id and were not merely
/// scheduled or later called off) created within a date range — the eShop side of the reconciliation.
/// Scheduled and canceled messages are excluded because they never went out, so their absence from the
/// provider's sent-message list is not a discrepancy.
/// </summary>
public sealed class NotificationsWithProviderSidBetweenSpecification : Specification<Notification>
{
    public NotificationsWithProviderSidBetweenSpecification(DateTimeOffset fromInclusive, DateTimeOffset toInclusive)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.Status != NotificationStatus.Scheduled &&
            n.Status != NotificationStatus.Canceled &&
            n.CreatedAt >= fromInclusive &&
            n.CreatedAt <= toInclusive);
    }
}
