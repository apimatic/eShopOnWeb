using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-ups for an order that are still scheduled with the provider (not yet gone out),
/// so they can be called off when the order is cancelled.
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<Notification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderStatus == "scheduled");
    }
}
