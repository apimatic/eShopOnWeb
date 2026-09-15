using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Follow-up messages for an order that are still queued with the provider (scheduled, not yet sent) and
/// so can still be called off.
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<Notification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.IsScheduled &&
            n.ProviderMessageSid != null &&
            n.Status == NotificationStatus.Scheduled);
    }
}
