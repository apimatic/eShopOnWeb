using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-up notifications for an order that are still scheduled with the provider —
/// i.e. queued to go out later and eligible to be called off (as when the order is cancelled).
/// </summary>
public sealed class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.DeliveryStatus == DeliveryStatuses.Scheduled
            && n.ProviderMessageSid != null);
    }
}
