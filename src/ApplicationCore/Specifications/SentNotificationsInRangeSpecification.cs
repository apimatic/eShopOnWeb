using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it sent within a send-time range — filtered on <see cref="OrderNotification.SentAtUtc"/>
/// (the message send time, the same clock the provider's DateSent filter uses), never the row-creation column.
/// </summary>
public class SentNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.SentAtUtc != null && n.SentAtUtc >= from && n.SentAtUtc <= to);
    }
}
