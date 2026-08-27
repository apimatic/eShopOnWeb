using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PendingFollowUpNotificationsSpec : Specification<OrderNotification>
{
    public PendingFollowUpNotificationsSpec(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && (n.ProviderStatus == "pending"
                || n.ProviderStatus == "accepted"
                || n.ProviderStatus == "queued"
                || n.ProviderStatus == "scheduled"
                || n.ProviderStatus == "sending"));
    }
}
