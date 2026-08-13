using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-ups for an order that were queued with the provider and are still scheduled
/// (not yet sent), so they can be called off when the order is cancelled.
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<SmsNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.IsScheduled
            && n.MessageSid != null
            && n.Status == "scheduled");
    }
}
