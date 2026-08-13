using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-ups for an order that are still scheduled at the provider and have not yet gone out —
/// the messages that must be called off when the order is cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            n.ProviderStatus == MessageStatus.Scheduled);
    }
}
