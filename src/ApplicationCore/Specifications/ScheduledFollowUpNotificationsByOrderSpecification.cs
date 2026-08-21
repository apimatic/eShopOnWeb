using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ScheduledFollowUpNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && (n.ProviderStatus == "scheduled"
                || n.ProviderStatus == "accepted"
                || n.ProviderStatus == "queued"));
    }
}
