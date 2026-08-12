using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// eShop's own record of messages handed to the provider (a SID was assigned) within a period —
/// the eShop side of the reconciliation.
/// </summary>
public class SentNotificationsInPeriodSpecification : Specification<OrderNotification>
{
    public SentNotificationsInPeriodSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedAt >= from
                         && n.CreatedAt <= to);
    }
}
