using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-up messages for an order that are still scheduled with the
/// provider (i.e. have not gone out yet) and can therefore still be cancelled.
/// </summary>
public class ScheduledFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId &&
            n.Type == NotificationType.DeliveryFollowUp &&
            n.MessageSid != null &&
            n.ProviderStatus == "scheduled");
    }
}
