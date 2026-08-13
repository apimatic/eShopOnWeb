using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider within a window: those that were given a
/// provider identifier and whose send timestamp falls inside <c>[from, to]</c>. Used as the
/// eShop side of the reconciliation report.
/// </summary>
public class OrderNotificationsSentBetweenSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.SentAt != null
                         && n.SentAt >= from
                         && n.SentAt <= to);
    }
}
