using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider (they carry a message SID) whose creation
/// falls in a date range — the eShop side of a reconciliation over that range.
/// </summary>
public class NotificationsWithSidInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsWithSidInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.MessageSid != null
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
