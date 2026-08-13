using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop handed to the provider (they carry a provider message id) within a date range,
/// for reconciliation against the provider's own record.
/// </summary>
public class SentNotificationsInRangeSpecification : Specification<Notification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
