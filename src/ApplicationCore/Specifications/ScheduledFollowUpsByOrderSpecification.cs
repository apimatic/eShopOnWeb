using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-ups for an order that are still scheduled with the provider and have not yet
/// gone out — the ones that must be called off when the order is cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsScheduled
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.DeliveryStatus == NotificationDeliveryStatus.Scheduled
                         && n.ProviderMessageSid != null);
    }
}
