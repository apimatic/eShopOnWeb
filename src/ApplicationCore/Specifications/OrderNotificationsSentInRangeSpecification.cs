using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications eShop believes it handed to the provider within a range: those that carry a provider
/// SID, were created in the window, and were not merely scheduled/cancelled or rejected before sending.
/// Used as the eShop side of reconciliation.
/// </summary>
public class OrderNotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSentInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedAt >= from && n.CreatedAt <= to &&
            n.DeliveryStatus != NotificationDeliveryStatus.Scheduled &&
            n.DeliveryStatus != NotificationDeliveryStatus.Canceled &&
            n.DeliveryStatus != NotificationDeliveryStatus.SendFailed);
    }
}
