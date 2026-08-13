using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Scheduled follow-up messages for an order that have not yet gone out — the ones that must be
/// called off with the provider when the order is cancelled.
/// </summary>
public class PendingScheduledNotificationsByOrderSpecification : Specification<Notification>
{
    public PendingScheduledNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduled
            && n.ProviderMessageSid != null
            && n.DeliveryStatus == NotificationDeliveryStatus.Scheduled);
    }
}
