using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery-feedback follow-ups for an order that are still queued with the provider (not yet sent) and
/// so can still be called off.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Type == NotificationType.DeliveryFeedback &&
            n.Status == NotificationStatuses.Scheduled &&
            n.ProviderMessageSid != null);
    }
}
