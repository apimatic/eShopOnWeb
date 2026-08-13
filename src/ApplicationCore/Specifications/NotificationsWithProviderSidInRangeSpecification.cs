using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider (they carry a provider SID) within
/// a created-date range, for lining up against the provider's own record during reconciliation.
/// </summary>
public sealed class NotificationsWithProviderSidInRangeSpecification : Specification<Notification>
{
    public NotificationsWithProviderSidInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.ProviderSid != null
                         && n.CreatedDate >= fromUtc
                         && n.CreatedDate <= toUtc);
    }
}
