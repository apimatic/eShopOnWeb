using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-up for an order that is still queued with the provider (scheduled) and so can
/// still be called off before it goes out.
/// </summary>
public sealed class PendingFollowUpByOrderSpecification : Specification<Notification>
{
    public PendingFollowUpByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Type == NotificationType.DeliveryFollowUp
                         && n.Status == NotificationStatus.Scheduled);
    }
}
