using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application has a provider message identifier for, recorded within a range.
/// Used by reconciliation to line eShop's own record up against the provider's.
/// </summary>
public sealed class SentOrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedAt >= fromUtc
                         && n.CreatedAt <= toUtc);
    }
}
