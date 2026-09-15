using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-ups for an order that are still scheduled at the provider (have a message id and
/// have not reached a terminal state) — the ones that must be called off when the order is cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.IsScheduled &&
            n.ProviderMessageSid != null &&
            n.Status == DeliveryStatus.Scheduled);
    }
}
