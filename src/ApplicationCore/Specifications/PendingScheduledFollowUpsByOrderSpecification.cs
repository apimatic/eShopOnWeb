using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The scheduled follow-up notifications for an order that are still queued at the provider (not yet
/// sent or cancelled) — the ones that must be called off when the order is cancelled.
/// </summary>
public class PendingScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.IsScheduled &&
            n.ProviderMessageSid != null &&
            n.DeliveryStatus == NotificationDeliveryState.Scheduled);
    }
}
