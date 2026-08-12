using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The order's delivery follow-ups that are still scheduled with the provider (a SID and
/// <see cref="DeliveryStatuses.Scheduled"/>). These are exactly the ones that must be called off
/// when an order is cancelled so the "how did delivery go?" message never reaches the shopper.
/// </summary>
public sealed class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Type == NotificationType.DeliveryFollowUp &&
            n.DeliveryStatus == DeliveryStatuses.Scheduled &&
            n.ProviderMessageSid != null);
    }
}
