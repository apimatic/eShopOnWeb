using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications this application handed a message id and that were created within a date range —
/// the eShop side of a reconciliation against the provider's record for the same range.
/// </summary>
public class SentNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.MessageSid != null
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
