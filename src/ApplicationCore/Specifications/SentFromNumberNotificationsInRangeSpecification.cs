using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The notifications eShop believes it sent from its own configured sending number within a range —
/// the eShop side of a reconciliation. Scheduled delivery follow-ups are excluded because they go out
/// through the Messaging Service, not the configured sending number, and so are not this report's traffic.
/// </summary>
public class SentFromNumberNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentFromNumberNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.Kind != NotificationKind.DeliveryFollowUp
            && n.CreatedAt >= from
            && n.CreatedAt <= to);
    }
}
