using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-ups for an order that are still scheduled with the provider (accepted, not yet sent),
/// i.e. the ones that must be called off when the order is cancelled.
/// </summary>
public sealed class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsScheduled
                         && n.ProviderMessageSid != null
                         && n.Status == NotificationDeliveryStatus.Scheduled);
    }
}
