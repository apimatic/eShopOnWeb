using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notifications eShop believes it sent within a date range — those that got a provider
/// message SID and were created within [from, to]. Used to line eShop's record up against the
/// provider's own for reconciliation.
/// </summary>
public sealed class SentOrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
