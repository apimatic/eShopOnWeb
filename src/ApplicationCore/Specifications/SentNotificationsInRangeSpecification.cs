using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's side of the reconciliation: notifications that carry a provider SID (so eShop believes the
/// provider accepted them) and were created within the reported range.
/// </summary>
public sealed class SentNotificationsInRangeSpecification : Specification<Notification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.ProviderSid != null
                         && n.CreatedDate >= fromUtc
                         && n.CreatedDate <= toUtc);
    }
}
