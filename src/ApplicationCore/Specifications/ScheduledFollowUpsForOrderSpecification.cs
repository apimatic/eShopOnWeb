using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-up messages for an order that are still scheduled with the
/// provider (i.e. have not gone out yet and can still be called off).
/// </summary>
public class ScheduledFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.NotificationType == NotificationType.DeliveryFollowUp
            && n.ProviderStatus == "scheduled"
            && n.ProviderMessageSid != null);
    }
}
