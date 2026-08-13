using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Follow-up messages for an order that are still scheduled and have not gone out yet — the ones
/// that must be called off if the order is cancelled.
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<Notification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.Status == NotificationStatus.Scheduled &&
            n.ProviderSid != null);
    }
}
