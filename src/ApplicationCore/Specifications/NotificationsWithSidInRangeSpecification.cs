using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications that carry a provider message SID and were created within the given range — the
/// "what eShop believes it sent" side of reconciliation.
/// </summary>
public class NotificationsWithSidInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsWithSidInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.CreatedDate >= fromUtc
            && n.CreatedDate <= toUtc);
    }
}
