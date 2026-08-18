using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it sent within a range — those that reached the provider (have a SID)
/// and were created in the window. Used as the eShop side of a reconciliation run.
/// </summary>
public sealed class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.MessageSid != null && n.CreatedAt >= fromUtc && n.CreatedAt <= toUtc);
    }
}
