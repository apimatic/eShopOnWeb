using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpNotificationsSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpNotificationsSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            n.ProviderStatus == "scheduled");
    }
}
