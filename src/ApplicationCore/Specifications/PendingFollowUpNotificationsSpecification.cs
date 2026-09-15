using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpNotificationsSpecification : Specification<OrderNotification>
{
    public PendingFollowUpNotificationsSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderStatus == "scheduled" &&
            n.ProviderMessageSid != null);
    }
}
