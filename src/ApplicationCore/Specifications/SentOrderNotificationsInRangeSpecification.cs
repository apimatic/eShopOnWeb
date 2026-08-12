using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications the shop believes it handed to the provider (they carry a provider SID and are not merely
/// scheduled/cancelled) created within an inclusive range — the eShop side of the reconciliation.
/// </summary>
public sealed class SentOrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.Status != NotificationDeliveryStatus.Scheduled
                         && n.Status != NotificationDeliveryStatus.Canceled
                         && n.CreatedDate >= from
                         && n.CreatedDate <= to);
    }
}
