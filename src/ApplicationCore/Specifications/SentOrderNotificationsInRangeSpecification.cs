using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider (they carry a provider identifier) and that
/// were created within a range. Used to line eShop's own record up against the provider's during
/// reconciliation.
/// </summary>
public class SentOrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedDate >= from
                         && n.CreatedDate <= to);
    }
}
