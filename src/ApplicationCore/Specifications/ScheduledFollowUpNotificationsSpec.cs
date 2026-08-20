using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpNotificationsSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpNotificationsSpec(int orderId)
    {
        Query.Where(notification =>
            notification.OrderId == orderId &&
            notification.Kind == NotificationKind.DeliveryFollowUp &&
            notification.ProviderMessageSid != null &&
            (notification.ProviderStatus == "scheduled" || notification.ProviderStatus == "accepted"));
    }
}
