using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it actually sent within a date range, for reconciliation
/// against the provider's own record of sent messages. A message carries a SID once it reaches
/// the provider; a message that is only scheduled, was cancelled before it went out, or never
/// reached the provider is not something eShop believes it sent, so those are excluded.
/// </summary>
public sealed class OrderNotificationsSentBetweenSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentBetweenSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedAt >= fromUtc
                         && n.CreatedAt <= toUtc
                         && n.DeliveryStatus != DeliveryStatuses.Scheduled
                         && n.DeliveryStatus != DeliveryStatuses.Canceled
                         && n.DeliveryStatus != DeliveryStatuses.SendFailed
                         && n.DeliveryStatus != DeliveryStatuses.Pending);
    }
}
